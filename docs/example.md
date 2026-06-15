## Example

On the client-side:

```js
const url = 'https://localhost:8443/echo';
const transport = new WebTransport(url);
await transport.ready;
const reader = transport.datagrams.readable.getReader();
const decoder = new TextDecoder('utf-8');
const { value, done } = await reader.read();
let data = decoder.decode(value);
console.log(data);
```

On the server-side:
```clj
(require '[com.structmap.webtransport.server :as wt])

(defn myhandler [session]
  (println (format "new session: %s" session))
  (wt/send session (wt/datagram "hello world"))
  (doseq [evt (wt/events session)]
     (when (wt/datagram? evt)
           (println "echoing datagram")
           (wt/send session evt))))

(def server
  (wt/create-server
    #::wt{:host "localhost"
          :port 8443
          :handler #'myhandler
          :tls ["cert.pem" "key.pem"]}))

(wt/start server)
```

On the server-side:
```java
var server = new Server(8443, "cert.pem", "key.pem");
server.logCallback = EchoServer::log_callback;
server.connectionValidator = EchoServer::connection_validator;
server.sessionHandler = EchoServer::session_handler;
```
