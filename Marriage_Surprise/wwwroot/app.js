const weddingStart = new Date("2026-04-23T10:30:00+05:30");

function setText(id, text) {
  const el = document.getElementById(id);
  if (el) {
    el.textContent = text;
  }
}

function formatDateTime(isoOrDate) {
    const date = new Date(isoOrDate + "Z");

    return date.toLocaleString("en-IN", {
        timeZone: "Asia/Kolkata",
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function startCountdown() {
  function tick() {
    const now = new Date();
    const diff = weddingStart.getTime() - now.getTime();

    if (diff <= 0) {
      setText("days", "0");
      setText("hours", "0");
      setText("minutes", "0");
      return;
    }

    const totalMinutes = Math.floor(diff / 60000);
    const days = Math.floor(totalMinutes / (60 * 24));
    const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
    const minutes = totalMinutes % 60;

    setText("days", String(days));
    setText("hours", String(hours));
    setText("minutes", String(minutes));
  }

  tick();
  setInterval(tick, 30000);
}

async function api(path, options) {
  const response = await fetch(path, options);
  const raw = await response.text();
  let payload = null;

  if (raw) {
    try {
      payload = JSON.parse(raw);
    } catch {
      payload = null;
    }
  }

  if (!response.ok) {
    const messageFromJson = payload && typeof payload === "object" ? payload.error : null;
    const fallback = raw || `${response.status} ${response.statusText}` || "Request failed.";
    throw new Error(messageFromJson || fallback);
  }
  return payload;
}

function renderWishes(items) {
  const wrap = document.getElementById("wishFeed");
  if (!wrap) {
    return;
  }

  if (!items.length) {
    wrap.innerHTML = "<p>No wishes yet. Be the first to bless the couple.</p>";
    return;
  }

  wrap.innerHTML = items.map((w) => {
    return `
      <article class="feed-item">
        <p><strong>${escapeHtml(w.guestName)}</strong>
          ${w.isDeveloper ? '<span class="badge">Author/Hemanth</span>' : ""}
        </p>
        <p>${escapeHtml(w.message)}</p>
        <div class="wish-item-footer">
          <small>${formatDateTime(w.createdAtUtc)}</small>
          <button type="button" class="danger-btn" data-delete-message-id="${w.id}">Delete</button>
        </div>
      </article>
    `;
  }).join("");
}

function renderMemories(items) {
  const wrap = document.getElementById("memoryFeed");
  if (!wrap) {
    return;
  }

  if (!items.length) {
    wrap.innerHTML = "<p>No memories uploaded yet.</p>";
    return;
  }

    wrap.innerHTML = items.map((m) => {
        return `
    <article class="feed-item">
      <img src="${m.imageBase64}" alt="Wedding memory by ${escapeHtml(m.guestName)}">
      <p><strong>${escapeHtml(m.guestName)}</strong></p>
      <p>${escapeHtml(m.caption)}</p>

      <div class="wish-item-footer">
        <small>${formatDateTime(m.createdAtUtc)}</small>
        <button type="button" class="danger-btn" data-delete-memory-id="${m.id}">
          Delete
        </button>
      </div>
    </article>
  `;
    }).join("");
}

async function refreshLiveData() {
  const [messages, memories] = await Promise.all([
    api("/api/live/messages"),
    api("/api/live/memories")
  ]);
  renderWishes(messages);
  renderMemories(memories);
}

function attachWishForm() {
  const form = document.getElementById("wishForm");
  if (!form) {
    return;
  }

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const status = document.getElementById("wishStatus");
    status.textContent = "Posting...";

    try {
      await api("/api/live/messages", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          guestName: document.getElementById("wishGuestName").value,
          message: document.getElementById("wishMessage").value,
          isDeveloper: document.getElementById("isDeveloper").checked
        })
      });
      form.reset();
      status.textContent = "Wish posted successfully!";
      await refreshLiveData();
    } catch (err) {
      status.textContent = err.message;
    }
  });
}

function attachMemoryForm() {
  const form = document.getElementById("memoryForm");
  if (!form) {
    return;
  }

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const status = document.getElementById("memoryStatus");
    status.textContent = "Uploading...";

    try {
      const data = new FormData();
      data.append("guestName", document.getElementById("memoryGuestName").value);
      data.append("caption", document.getElementById("memoryCaption").value);
      data.append("memoryImage", document.getElementById("memoryImage").files[0]);

      await api("/api/live/memories", {
        method: "POST",
        body: data
      });
      form.reset();
      status.textContent = "Memory uploaded!";
      await refreshLiveData();
    } catch (err) {
      status.textContent = err.message;
    }
  });
}

function attachWishDeleteHandlers() {
  const feed = document.getElementById("wishFeed");
  if (!feed) {
    return;
  }

  feed.addEventListener("click", async (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
      return;
    }

    const deleteButton = target.closest("[data-delete-message-id]");
    if (!deleteButton) {
      return;
    }

    const messageId = deleteButton.getAttribute("data-delete-message-id");
    if (!messageId) {
      return;
    }

    const emailInput = window.prompt("Enter your Gmail to delete this message:");
    if (emailInput === null) {
      return;
    }

    const status = document.getElementById("wishStatus");
    status.textContent = "Verifying and deleting...";

    try {
      await api(`/api/live/messages/${messageId}`, {
        method: "DELETE",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: emailInput.trim() })
      });
      status.textContent = "Message deleted.";
      await refreshLiveData();
    } catch (err) {
      status.textContent = err.message === "Unauthorized"
        ? "Entered Gmail is not authorized to delete."
        : err.message;
    }
  });
}

function escapeHtml(input) {
  return String(input)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

async function init() {
  startCountdown();
  attachWishForm();
  attachMemoryForm();
    attachWishDeleteHandlers();
    attachMemoryDeleteHandlers(); 
  await refreshLiveData();
  setInterval(async () => {
    try {
      await refreshLiveData();
    } catch (err) {
      console.error("Refresh failed:", err);
    }
  }, 7000);
}

init().catch((err) => {
  console.error("Initialization failed:", err);
});

function attachMemoryDeleteHandlers() {
    const feed = document.getElementById("memoryFeed");
    if (!feed) return;

    feed.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) return;

        const deleteButton = target.closest("[data-delete-memory-id]");
        if (!deleteButton) return;

        const memoryId = deleteButton.getAttribute("data-delete-memory-id");
        if (!memoryId) return;

        const emailInput = window.prompt("Enter your Gmail to delete this memory:");
        if (emailInput === null) return;

        const status = document.getElementById("memoryStatus");
        status.textContent = "Verifying and deleting...";

        try {
            await api(`/api/live/memories/${memoryId}`, {
                method: "DELETE",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ email: emailInput.trim() })
            });

            status.textContent = "Memory deleted.";
            await refreshLiveData();
        } catch (err) {
            status.textContent = err.message === "Unauthorized"
                ? "Entered Gmail is not authorized to delete."
                : err.message;
        }
    });
}