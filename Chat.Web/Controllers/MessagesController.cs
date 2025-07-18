using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Chat.Web.Data;
using Chat.Web.Models;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Microsoft.AspNetCore.SignalR;
using Chat.Web.Hubs;
using Chat.Web.ViewModels;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _http;
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IHubContext<ChatHub> _hubContext;

        public MessagesController(ApplicationDbContext context,
            IMapper mapper,
            IHubContext<ChatHub> hubContext,
            IConfiguration config, 
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _mapper = mapper;
            _hubContext = hubContext;
            _configuration = config;
            _http = httpClientFactory.CreateClient();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Room>> Get(int id)
        {
            var message = await _context.Messages.FindAsync(id);
            if (message == null)
                return NotFound();

            var messageViewModel = _mapper.Map<Message, MessageViewModel>(message);
            return Ok(messageViewModel);
        }

        [HttpGet("Room/{roomName}")]
        public IActionResult GetMessages(string roomName)
        {
            var room = _context.Rooms.FirstOrDefault(r => r.Name == roomName);
            if (room == null)
                return BadRequest();

            var messages = _context.Messages.Where(m => m.ToRoomId == room.Id)
                .Include(m => m.FromUser)
                .Include(m => m.ToRoom)
                .OrderByDescending(m => m.Timestamp)
                .Take(20)
                .AsEnumerable()
                .Reverse()
                .ToList();

            var messagesViewModel = _mapper.Map<IEnumerable<Message>, IEnumerable<MessageViewModel>>(messages);

            foreach (var messageViewModel in messagesViewModel)
            {
                try
                {
                    messageViewModel.Content = DecryptBackend(messageViewModel.Content);
                }
                catch (Exception ex)
                {
                    messageViewModel.Content = "[Failed to decrypt]";
                }
            }

            return Ok(messagesViewModel);
        }

        [HttpPost]
        public async Task<ActionResult<Message>> Create(MessageViewModel viewModel)
        {
            TimeZoneInfo indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            DateTime indiaTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone);

            var user = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name);
            var room = _context.Rooms.FirstOrDefault(r => r.Name == viewModel.Room);
            if (room == null)
                return BadRequest();

            var tempContent = viewModel.GifUrl != null
                                ? $"<img src=\"{viewModel.GifUrl}\" />"
                                : viewModel.Content;

            var doubleEncrypted = EncryptBackend(tempContent);

            var msg = new Message
            {
                Content = doubleEncrypted,
                FromUser = user,
                ToRoom = room,
                Timestamp = indiaTime
            };

            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            var backendDecrypted = DecryptBackend(msg.Content);
            msg.Content = backendDecrypted; // still frontend-encrypted

            // Broadcast the message
            var createdMessage = _mapper.Map<Message, MessageViewModel>(msg);
            await _hubContext.Clients.Group(room.Name).SendAsync("newMessage", createdMessage);

            if (!string.IsNullOrWhiteSpace(viewModel.GifUrl))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var http = new HttpClient();
                        var bytes = await http.GetByteArrayAsync(viewModel.GifUrl);
                        var base64 = Convert.ToBase64String(bytes);
                        var base64Img = $"<img src='data:image/gif;base64,{base64}' />";

                        msg.Content = base64Img;
                        _context.Messages.Update(msg);
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        // Optional: log error
                    }
                });
            }

            return CreatedAtAction(nameof(Get), new { id = msg.Id }, createdMessage);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var message = await _context.Messages
                .Include(u => u.FromUser)
                .Where(m => m.Id == id && m.FromUser.UserName == User.Identity.Name)
                .FirstOrDefaultAsync();

            if (message == null)
                return NotFound();

            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("removeChatMessage", message.Id);

            return Ok();
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            var apiKey = _configuration["Tenor:ApiKey"];
            var url = $"https://tenor.googleapis.com/v2/search?q={Uri.EscapeDataString(query)}&key={apiKey}&limit=12";

            var response = await _http.GetStringAsync(url);
            return Content(response, "application/json");
        }

        [HttpGet("trending")]
        public async Task<IActionResult> GetTrending()
        {
            var apiKey = _configuration["Tenor:ApiKey"];
            var url = $"https://tenor.googleapis.com/v2/featured?key={apiKey}&limit=12";

            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, "Failed to fetch trending gifs from Tenor.");
            }

            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }

        string EncryptBackend(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(_configuration["Encryption:backendKey"].PadRight(32).Substring(0, 32));
            aes.IV = new byte[16];

            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            var inputBuffer = Encoding.UTF8.GetBytes(plainText);

            var encrypted = encryptor.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);
            return Convert.ToBase64String(encrypted);
        }

        string DecryptBackend(string encryptedText)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(_configuration["Encryption:backendKey"].PadRight(32).Substring(0, 32));
            aes.IV = new byte[16];

            var buffer = Convert.FromBase64String(encryptedText);
            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var decrypted = decryptor.TransformFinalBlock(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
