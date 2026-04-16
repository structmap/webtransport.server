;; Ivar Refsdal wrote the original and better version of this code here:
;; https://github.com/ivarref/locksmith/blob/main/src/com/github/ivarref/locksmith.clj
(ns com.structmap.http3.util
  (:import (java.io File)
           (java.util.concurrent TimeUnit)
           (okhttp3.tls HeldCertificate HeldCertificate$Builder)))

(defn spit' [x y]
  (if (.exists (File. x))
    (do
      (println (format "Error: refusing to overwrite %s" x))
      false)
    (do
      (spit x y)
      (println (format "Wrote %s" x))
      true)))

(defn write-certs!
  [{:keys [duration-days server-hostname]
    :or   {duration-days 13 server-hostname "localhost"}}]
  (let [^HeldCertificate rootCertificate (-> (HeldCertificate$Builder.)
                                             (.certificateAuthority 0)
                                             (.duration duration-days TimeUnit/DAYS)
                                             (.build))
        ^HeldCertificate serverCertificate (-> (HeldCertificate$Builder.)
                                               (.signedBy rootCertificate)
                                               (.addSubjectAlternativeName server-hostname)
                                               (.duration duration-days TimeUnit/DAYS)
                                               (.build))]
    (and
      (spit' "ca.pem" (.certificatePem rootCertificate))
      (spit' "cert.pem" (.certificatePem serverCertificate))
      (spit' "key.pem" (.privateKeyPkcs8Pem serverCertificate))
      :ok)))
