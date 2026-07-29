mergeInto(LibraryManager.library, {
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
