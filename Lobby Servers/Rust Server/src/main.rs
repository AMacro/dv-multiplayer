mod config;
mod server;
mod state;
mod handlers;
mod ssl;
mod utils;

use crate::config::read_or_create_config;
use crate::state::AppState;
use crate::ssl::setup_ssl;
use actix_web::{web, App, HttpServer};
use std::sync::{Arc, Mutex};
use tokio::time::{interval, Duration};

#[tokio::main]
async fn main() -> std::io::Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();

    let config = read_or_create_config();
    let state = AppState {
        servers: Arc::new(Mutex::new(std::collections::HashMap::new())),
    };

    let cleanup_state = state.clone();
    let config_clone = config.clone();

    tokio::spawn(async move {
        let mut interval = interval(Duration::from_secs(60));
        loop {
            interval.tick().await;
            let now = std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_secs();
            if let Ok(mut servers) = cleanup_state.servers.lock() {
                let keys_to_remove: Vec<String> = servers
                    .iter()
                    .filter_map(|(key, info)| {
                        if now - info.last_update > config_clone.timeout {
                            Some(key.clone())
                        } else {
                            None
                        }
                    })
                    .collect();
                for key in keys_to_remove {
                    servers.remove(&key);
                }
            }
        }
    });

    let server = {
        let server_builder = HttpServer::new(move || {
            App::new()
                .app_data(web::Data::new(state.clone()))
                .route("/add_game_server", web::post().to(handlers::add_server))
                .route("/update_game_server", web::post().to(handlers::update_server))
                .route("/remove_game_server", web::post().to(handlers::remove_server))
                .route("/list_game_servers", web::get().to(handlers::list_servers))
        });

        if config.ssl_enabled {
            let ssl_builder = setup_ssl(&config)?;
            server_builder
                .bind_openssl(format!("0.0.0.0:{}", config.port), (move || ssl_builder)())?
        } else {
            server_builder.bind(format!("0.0.0.0:{}", config.port))?
        }
    };

    // Start the server
    server.run().await
}