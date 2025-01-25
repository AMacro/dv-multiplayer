use crate::config::Config;
use openssl::ssl::{SslAcceptor, SslFiletype, SslMethod};
use openssl::ssl::SslAcceptorBuilder;

pub fn setup_ssl(config: &Config) -> std::io::Result<SslAcceptorBuilder> {
    let mut builder = SslAcceptor::mozilla_intermediate(SslMethod::tls())?;
    builder.set_private_key_file(&config.ssl_key_path, SslFiletype::PEM)?;
    builder.set_certificate_chain_file(&config.ssl_cert_path)?;
    Ok(builder)
}
