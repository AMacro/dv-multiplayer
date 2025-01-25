use serde::{Deserialize, Serialize};
use std::fs::File;
use std::io::{Read, Write};

#[derive(Serialize, Deserialize, Clone)]
pub struct Config {
    pub port: u16,
    pub timeout: u64,
    pub ssl_enabled: bool,
    pub ssl_cert_path: String,
    pub ssl_key_path: String,
}

impl Default for Config {
    fn default() -> Self {
        Config {
            port: 8080,
            timeout: 120,
            ssl_enabled: false,
            ssl_cert_path: String::from("cert.pem"),
            ssl_key_path: String::from("key.pem"),
        }
    }
}

pub fn read_or_create_config() -> Config {
    let config_path = "config.json";
    let mut config = Config::default();

    if let Ok(mut file) = File::open(config_path) {
        let mut contents = String::new();
        if file.read_to_string(&mut contents).is_ok() {
            if let Ok(parsed_config) = serde_json::from_str(&contents) {
                config = parsed_config;
            }
        }
    } else {
        if let Ok(mut file) = File::create(config_path) {
            let _ = file.write_all(serde_json::to_string_pretty(&config).unwrap().as_bytes());
        }
    }

    config
}
