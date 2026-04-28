# WebTransport Server

WebTransport is a new browser Javascript API and a part of the HTTP3 spec (RFC
9221). To handle WebTransport connections on the server-side you need a partial
HTTP3 implementation on top of a QUIC engine.

This project wraps libwtf for Java, C# and Clojure programs to allow you to
respond to handle WebTransport streams and datagrams. The packages include
native dependencies (libwtf and MsQuic) so they are platform-specific.

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
    {:host "localhost"
     :port 8443
     :handler #'myhandler
     :tls ["cert.pem" "key.pem"]}))

(wt/start server)
```

The JVM implementation uses a virtual thread per WebTransport session. For .NET
the implementation starts one task per session on the default scheduler. To
start with JDK 25+ and .NET 10 are the only supported platform versions.

## Docs

See link.

## Setup

Encryption is an integrated part of the HTTP3 protocol. TLS is not a layer can
be disabled for local development convenience. To get a WebTransport connection
in Safari on Mac or iOS and Chrome or Edge on Windows the certificate key pair:
 - must be ECDSA not RSA
 - must use NIST P-256 (prime256v1 / secp256r1) curve
 - must use SHA-256 signature hash
 - must be signed by a CA (certificate authority) certificate
 - must be less than 14 days old
 - must have a hostname SAN (subject alternative name) e.g. "localhost"

So for local development you need to generate two certificates and two keys but
you can (and should) throw away the CA private key. This repo has a util script
based on Ivar Refsdal's excellent locksmith tool which wraps Square's okhttp
library. For local use only, not production!

```
$ clojure -X:util:write-certs
Wrote ca.pem
Wrote cert.pem
Wrote key.pem
```

If you're on macOS then open the Keychain Access app, choose System then File >
Import items... to locate and select `ca.pem` Then you need to click on the
certificate to open the 'Get Info' modal and under Trust change the setting for
Secure Sockets Layer (SSL) to "Always Trust". When you close the window you
will be prompted to enter your password to persist the changes.

If you're on Windows and using Chrome or Edge then go to Settings > Privacy and
security > Security > Manage certificates to import your `ca.pem` file.

## Development

```
dotnet run --project src/main/csharp -p:Platform=x64
```
