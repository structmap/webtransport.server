# http3

## getting started

HTTPS works on TCP wrapped in TLS but HTTP3 is different: it is UDP-based and
TLS 1.3 is an integrated part of the protocol. It is not a wrapper that can be
disabled for local development convenience.

You need a cert and key for HTTP3 and a plain old RSA certificate will work
fine. If you are doing WebTransport then browsers impose specific requirements.
There is a lot of confusion about these and the will change over time but
presently what works for me in Safari on Mac and Chrome / Edge on Windows is:
 - must be ECDSA not RSA
 - must use NIST P-256 (prime256v1 / secp256r1) curve
 - must use SHA-256 signature hash
 - must be signed by a CA (certificate authority) certificate
 - must be less than 14 days old
 - must have a hostname SAN (subject alternative name) e.g. "localhost"

So for local development you need two certificates and two keys but you can
(and should) throw away the CA private key. To generate a ca.pem, cert.pem and
key.pem for local use only this repo has a util script based on Ivar Refsdal's
excellent locksmith tool which wraps Square's okhttp library.

```
$ clojure -X:util:write-certs
Wrote ca.pem
Wrote cert.pem
Wrote key.pem
```

If you're on macOS then open the Keychain Access app, choose System then File >
Import items... to locate and select `ca.pem`. Then you need to click on the
certificate to open the 'Get Info' modal and under Trust change the setting for
Secure Sockets Layer (SSL) to "Always Trust". When you close the window you
will be prompted to enter your password to persist the changes.

If you're on Windows and using Chrome / Edge then go to Settings > Privacy and
security > Security > Manage certificates. From there you can import ca.pem
which is convenient.
