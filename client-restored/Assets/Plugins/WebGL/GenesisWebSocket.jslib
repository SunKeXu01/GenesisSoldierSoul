mergeInto(LibraryManager.library, {
  GenesisEnterGameplayMode: function () {
    var canvas = Module.canvas;
    if (!canvas) {
      return;
    }

    window.__genesisGameplayMode = true;
    window.__genesisMouseDeltaX = 0;
    window.__genesisMouseDeltaY = 0;
    canvas.style.cursor = "none";

    if (!window.__genesisMouseTrackingInstalled) {
      window.__genesisMouseTrackingInstalled = true;
      document.addEventListener("mousemove", function (event) {
        if (!window.__genesisGameplayMode) {
          return;
        }
        window.__genesisMouseDeltaX +=
          event.movementX || event.mozMovementX || event.webkitMovementX || 0;
        window.__genesisMouseDeltaY +=
          event.movementY || event.mozMovementY || event.webkitMovementY || 0;
      });
      canvas.addEventListener("mousedown", function () {
        if (!window.__genesisGameplayMode) {
          return;
        }
        canvas.style.cursor = "none";
      });
    }
  },

  GenesisConsumeMouseDeltaX: function () {
    var value = window.__genesisMouseDeltaX || 0;
    window.__genesisMouseDeltaX = 0;
    return value;
  },

  GenesisConsumeMouseDeltaY: function () {
    var value = window.__genesisMouseDeltaY || 0;
    window.__genesisMouseDeltaY = 0;
    return value;
  },

  GenesisExitGameplayMode: function () {
    window.__genesisGameplayMode = false;
    window.__genesisMouseDeltaX = 0;
    window.__genesisMouseDeltaY = 0;
    if (Module.canvas) {
      Module.canvas.style.cursor = "default";
    }
  },

  GenesisSocketConnect: function (urlPointer, gameObjectPointer) {
    if (!window.__genesisSockets) {
      window.__genesisSockets = { nextId: 1, sockets: {} };
    }

    var url = UTF8ToString(urlPointer);
    var gameObject = UTF8ToString(gameObjectPointer);
    if (!url) {
      var scheme = window.location.protocol === "https:" ? "wss:" : "ws:";
      url = scheme + "//" + window.location.host + "/game";
    }

    var id = window.__genesisSockets.nextId++;
    var socket = new WebSocket(url);
    window.__genesisSockets.sockets[id] = socket;

    socket.onopen = function () {
      SendMessage(gameObject, "OnSocketOpen", id.toString());
    };
    socket.onmessage = function (event) {
      SendMessage(gameObject, "OnSocketMessage", event.data);
    };
    socket.onerror = function () {
      SendMessage(gameObject, "OnSocketError", "websocket_error");
    };
    socket.onclose = function (event) {
      delete window.__genesisSockets.sockets[id];
      SendMessage(
        gameObject,
        "OnSocketClose",
        event.code.toString() + ":" + event.reason,
      );
    };
    return id;
  },

  GenesisSocketSend: function (id, messagePointer) {
    var state = window.__genesisSockets;
    var socket = state && state.sockets[id];
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(UTF8ToString(messagePointer));
    }
  },

  GenesisSocketClose: function (id) {
    var state = window.__genesisSockets;
    var socket = state && state.sockets[id];
    if (socket) {
      socket.close(1000, "client_close");
    }
  },
});
