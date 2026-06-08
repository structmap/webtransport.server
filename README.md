# WebTransport Server

To handle WebTransport streams and datagrams on the server side one needs a
partial HTTP3 implementation on top of a QUIC engine. This project wraps libwtf
for Java, C# and Clojure programs and provides platform-specific packages with
the native dependencies pre-compiled for Windows, macOS and Linux.

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
mkdir -p src/test/csharp/Samples/bin/arm64/Debug/net10.0
cp -v *pem src/test/csharp/Samples/bin/arm64/Debug/net10.0
dotnet run --project src/test/csharp/Samples -p:Platform=arm64 --configuration Debug --framework net10.0 --environment DYLD_LIBRARY_PATH="$HOME/.homebrew/lib"
```

### Native

On Linux and macOS you will need to provide MsQuic and OpenSSL. This usually
means `brew install libmsquic` on macOS and `apt install libmsquic` on Ubuntu
once you have added the [Linux Software Repository for Microsoft Products](https://learn.microsoft.com/en-us/linux/packages).

On a Mac, depending on how you have Homebrew configured, you may need to tell
your IDE that to launch any sample programs with the DYLD_LIBRARY_PATH
environment variable set to the directory which contains the dylib files. In
recent years SIP (System Integrity Protection) has made this tricky to do from
the command-line but IntelliJ and Rider can set DYLD_LIBRARY_PATH reliably.

I use `~/.homebrew` for my Brew setup so in run/debug configuration settings I
add `DYLD_LIBRARY_PATH=$HOME$/.homebrew/lib` to the environment variables.

On Windows you should not need to specify paths to native dependencies because
the libwtf packages (for JVM and .NET) bundle a self-contained msquic.dll which
does not load OpenSSL at runtime (it builds its own version at compile time).
