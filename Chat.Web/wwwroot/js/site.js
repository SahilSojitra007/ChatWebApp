$(function () {
    let gifsLoaded = false;

    $('ul#users-list').on('click', 'li', function () {
        var username = $(this).data("username");
        var input = $('#message-input');

        var text = input.val();
        if (text.startsWith("/")) {
            text = text.split(")")[1];
        }

        text = "/private(" + username + ") " + text.trim();
        input.val(text);
        input.change();
        input.focus();
    });

    document.getElementById('emojis-picker').addEventListener('emoji-click', function (event) {
        const emoji = event.detail.unicode;
        const input = document.getElementById('message-input');

        const start = input.selectionStart;
        const end = input.selectionEnd;
        const text = input.value;

        input.value = text.substring(0, start) + emoji + text.substring(end);
        input.focus();
        input.setSelectionRange(start + emoji.length, start + emoji.length);

        const ctx = ko.contextFor(input);
        if (ctx && ctx.$data && ko.isObservable(ctx.$data.message)) {
            ctx.$data.message(input.value);
        }
    });

    $("#btn-show-emojis").click(function () {
        $("#emojis-container").toggleClass("d-none");
        $("#emojis-picker").toggleClass("d-none");
    });

    $("#btn-show-gifs").click(function () {
        $("#gifs-container").toggleClass("d-none");
        $("#sub-gifs-container").toggleClass("d-none");
        $("#gifSearch").toggleClass("d-none");

        if (!gifsLoaded) {
            fetchTrendingGifs();
            gifsLoaded = true;
        }
    });

    $("#message-input, .messages-container, #btn-send-message, #emojis-container button").click(function () {
        $("#emojis-container").addClass("d-none");
        $("#emojis-picker").addClass("d-none");
        $("#gifs-container").addClass("d-none");
        $("#sub-gifs-container").addClass("d-none");
        $("#gifSearch").addClass("d-none");
    });

    $("#expand-sidebar").click(function () {
        $(".sidebar").toggleClass("open");
        $(".users-container").removeClass("open");
    });

    $("#expand-users-list").click(function () {
        $(".users-container").toggleClass("open");
        $(".sidebar").removeClass("open");
    });

    $(document).on("click", ".sidebar.open ul li a, #users-list li", function () {
        $(".sidebar, .users-container").removeClass("open");
    });

    $(".modal").on("shown.bs.modal", function () {
        $(this).find("input[type=text]:first-child").focus();
    });

    $('.modal').on('hidden.bs.modal', function () {
        $(".modal-body input:not(#newRoomName)").val("");
    });

    $(".alert .btn-close").on('click', function () {
        $(this).parent().hide();
    });

    $('body').tooltip({
        selector: '[data-bs-toggle="tooltip"]',
        delay: { show: 500 }
    });

    $("#remove-message-modal").on("shown.bs.modal", function (e) {
        const id = e.relatedTarget.getAttribute('data-messageId');
        $("#itemToDelete").val(id);
    });

    $(document).on("mouseenter", ".ismine", function () {
        $(this).find(".actions").removeClass("d-none");
    });

    $(document).on("mouseleave", ".ismine", function () {
        var isDropdownOpen = $(this).find(".dropdown-menu.show").length > 0;
        if (!isDropdownOpen)
            $(this).find(".actions").addClass("d-none");
    });

    $(document).on("hidden.bs.dropdown", ".actions .dropdown", function () {
        $(this).closest(".actions").addClass("d-none");
    });

    document.addEventListener('DOMContentLoaded', () => {
        const emojiBtn = document.getElementById('btn-show-emojis');
        const emojiPicker = document.getElementById('emojis-picker');
        const input = document.getElementById('chat-input');

        // Toggle emoji picker visibility
        emojiBtn.addEventListener('click', () => {
            emojiPicker.classList.toggle('d-none');
        });

        // Insert emoji when selected
        emojiPicker.addEventListener('emoji-click', (event) => {
            const emoji = event.detail.unicode;
            const start = input.selectionStart;
            const end = input.selectionEnd;
            input.value = input.value.substring(0, start) + emoji + input.value.substring(end);
            input.focus();
            input.setSelectionRange(start + emoji.length, start + emoji.length);
        });
    });

    function fetchTrendingGifs() {
        const url = `/api/messages/trending`;

        fetch(url)
            .then((res) => res.json())
            .then((data) => renderGifs(data.results))
            .catch((err) => {
                console.error("Failed to load trending gifs:", err);
            });
    }

    let debounceTimeOut = null;
    document.getElementById("gifSearch").addEventListener("input", function () {
        const query = this.value.trim();

        clearTimeout(debounceTimeOut);

        debounceTimeOut = setTimeout(() => {
            if (query.length > 1) {
                searchGIFs(query);
            } else {
                fetchTrendingGifs();
            }
        }, 300);
    });

    function searchGIFs(query) {
        const url = `/api/messages/search?query=${encodeURIComponent(query)}`;

        fetch(url)
            .then((res) => res.json())
            .then((data) => renderGifs(data.results))
            .catch((err) => {
                console.error("GIF search failed:", err);
            });
    }

    function renderGifs(gifs) {
        if (!gifs || !Array.isArray(gifs)) {
            console.warn("No GIFs to render or invalid response.");
            return;
        }

        const gifsContainer = document.getElementById("sub-gifs-container");
        gifsContainer.innerHTML = "";

        gifs.forEach((gif) => {
            const gifUrl = gif.media_formats?.gif?.url;

            const wrapper = document.createElement("div");
            wrapper.style.display = "flex";
            wrapper.style.alignItems = "center";
            wrapper.style.justifyContent = "center";
            wrapper.style.position = "relative";
            wrapper.style.width = "100%";
            wrapper.style.height = "150px";
            wrapper.style.overflow = "hidden";

            const loader = document.createElement("div");
            loader.className = "gif-loader";

            const img = document.createElement("img");
            img.src = gifUrl;
            img.className = "gif-thumb";
            img.style.display = "none";

            img.onload = function () {
                loader.style.display = "none";
                img.style.display = "block";
            };

            img.addEventListener("click", () => {
                $("#gifs-container").addClass("d-none");
                $("#sub-gifs-container").addClass("d-none");
                $("#gifSearch").addClass("d-none");

                setTimeout(() => {
                    const roomName = document.getElementById("joinedRoom").innerText.trim();
                    const imgTag = gifUrl;
                    const frontendKey = document.querySelector('meta[name="frontend-key"]')?.content;

                    function encryptFrontend(imgTag) {
                        return CryptoJS.AES.encrypt(imgTag, frontendKey).toString();
                    }

                    const encryptedMessage = encryptFrontend(imgTag);

                    fetch('/api/Messages', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({ room: roomName, gifurl: encryptedMessage })
                    }).then(res => {
                        if (!res.ok) throw new Error("Failed to send gif");
                    }).catch(err => {
                        console.error("API call failed:", err);
                    });
                })
            
            }, 2000);

            wrapper.appendChild(loader);
            wrapper.appendChild(img);
            gifsContainer.appendChild(wrapper);
        });

    }
});