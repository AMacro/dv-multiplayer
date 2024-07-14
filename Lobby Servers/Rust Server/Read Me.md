# Lobby Server - Rust

This is a [Rust](https://www.rust-lang.org/) implementation of the Derail Valley Lobby Server REST API service. The server can be run in either HTTP or HTTPS (SSL) modes (cert and key PEM files will need to be provided for SSL mode).

## Building the Code

To build the Lobby Server code, you'll need Rust, Cargo and OpenSSL installed on your system.


### Installing OpenSSL (Windows) 
OpenSSL can be installed as follows [[source](https://stackoverflow.com/a/70949736)]:
1. Install OpenSSL from [http://slproweb.com/products/Win32OpenSSL.html](http://slproweb.com/products/Win32OpenSSL.html) into `C:\Program Files\OpenSSL-Win64`
2. In an elevated terminal
```
$env:path = $env:path+  ";C:\Program Files\OpenSSL-Win64\bin" 
cd "C:\Program Files\OpenSSL-Win64"
mkdir certs
cd certs
wget https://curl.se/ca/cacert.pem -o cacert.pem
```
4. In the VSCode Rust Server terminal set the following environment variables
```
$env:OPENSSL_CONF='C:\Program Files\OpenSSL-Win64\bin\openssl.cfg'
$env:OPENSSL_NO_VENDOR=1
$env:RUSTFLAGS='-Ctarget-feature=+crt-static'
$env:SSL_CERT = 'C:\Program Files\OpenSSL-Win64\certs\cacert.pem'
$env:OPENSSL_DIR = 'C:\Program Files\OpenSSL-Win64'
$env:OPENSSL_LIB_DIR = "C:\Program Files\OpenSSL-Win64\lib\VC\x64\MD"
```


### Building
The code can be built using `cargo build --release` or built and run (for testing purposes) using `cargo run --release`

## Configuration Parameters
The server can be configured using a `config.json` file; if one is not supplied, the server will create one with the defaults.

Below are the available parameters along with their defaults:
-   `port` (u16): The port number on which the server will listen. Default: `8080`
-   `timeout` (u64): The time-out period in seconds for server removal. Default: `120`
-   `ssl_enabled` (bool): Whether SSL is enabled. Default: `false`
-   `ssl_cert_path` (string): Path to the SSL certificate file. Default: `"cert.pem"`
-   `ssl_key_path` (string): Path to the SSL private key file. Default: `"key.pem"`

To customise these parameters, create a `config.json` file in the project directory with the desired values.
Example `config.json`:
```json
{
  "port": 8080,
  "timeout": 120,
  "ssl_enabled": false,
  "ssl_cert_path": "cert.pem",
  "ssl_key_path": "key.pem"
}
```

