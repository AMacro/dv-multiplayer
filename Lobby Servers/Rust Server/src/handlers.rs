use actix_web::{web, HttpResponse, HttpRequest, Responder};
use serde::{Deserialize, Serialize};
use crate::state::AppState;
use crate::server::{ServerInfo, PublicServerInfo, AddServerResponse, validate_server_info};
use crate::utils::generate_private_key;
use uuid::Uuid;

#[derive(Deserialize)]
pub struct AddServerRequest {
    pub ip: Option<String>,
    pub port: u16,
    pub server_name: String,
    pub password_protected: bool,
    pub game_mode: u8,
    pub difficulty: u8,
    pub time_passed: String,
    pub current_players: u32,
    pub max_players: u32,
    pub required_mods: String,
    pub game_version: String,
    pub multiplayer_version: String,
    pub server_info: String,
}

pub async fn add_server(data: web::Data<AppState>, server_info: web::Json<AddServerRequest>, req: HttpRequest) -> impl Responder {
    let client_ip = req.connection_info().realip_remote_addr().unwrap_or("unknown").to_string();

    let ip = match server_info.ip.as_deref() {
        Some(ip_str) => {
            // Attempt to parse the IP address
            match ip_str.parse::<std::net::IpAddr>() {
                Ok(_) => ip_str.to_string(), // Valid IP address, use it
                Err(_) => client_ip.clone(), // Invalid IP address, use client IP
            }
        },
        None => client_ip.clone(), // server_info.ip is absent, use client IP
    };

    let private_key = generate_private_key(); // Generate a private key
    let info = ServerInfo {
        ip: ip.clone(),
        port: server_info.port,
        server_name: server_info.server_name.clone(),
        password_protected: server_info.password_protected,
        game_mode: server_info.game_mode,
        difficulty: server_info.difficulty,
        time_passed: server_info.time_passed.clone(),
        current_players: server_info.current_players,
        max_players: server_info.max_players,
        required_mods: server_info.required_mods.clone(),
        game_version: server_info.game_version.clone(),
        multiplayer_version: server_info.multiplayer_version.clone(),
        server_info: server_info.server_info.clone(),
        last_update: std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_secs(),
        private_key: private_key.clone(),
    };

    if let Err(e) = validate_server_info(&info) {
        log::error!("Validation failed: {}", e);
        return HttpResponse::BadRequest().json(e);
    }
    
    let game_server_id = Uuid::new_v4().to_string();
    let key = game_server_id.clone();
    match data.servers.lock() {
        Ok(mut servers) => {
            servers.insert(key.clone(), info);
            log::info!("Server added: {}", key);
            HttpResponse::Ok().json(AddServerResponse { game_server_id: key, private_key })
        }
        Err(_) => {
            log::error!("Failed to add server: {}", key);
            HttpResponse::InternalServerError().json("Failed to add server")
        }
    }
}

#[derive(Deserialize)]
pub struct UpdateServerRequest {
    pub game_server_id: String,
    pub private_key: String,
    pub current_players: u32,
    pub time_passed: String,
}

pub async fn update_server(data: web::Data<AppState>, server_info: web::Json<UpdateServerRequest>) -> impl Responder {
    let mut updated = false;
    match data.servers.lock() {
        Ok(mut servers) => {
            if let Some(info) = servers.get_mut(&server_info.game_server_id) {
                if info.private_key == server_info.private_key {
                    if server_info.current_players <= info.max_players {
                        info.current_players = server_info.current_players;
                        info.time_passed = server_info.time_passed.clone();
                        info.last_update = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_secs();
                        updated = true;
                    }
                } else {
                    return HttpResponse::Unauthorized().json("Invalid private key");
                }
            }
        }
        Err(_) => {
            log::error!("Failed to update server: {}", server_info.game_server_id);
            return HttpResponse::InternalServerError().json("Failed to update server");
        }
    }
    
    if updated {
        log::info!("Server updated: {}", server_info.game_server_id);
        HttpResponse::Ok().json("Server updated")
    } else {
        log::error!("Server not found or invalid current players: {}", server_info.game_server_id);
        HttpResponse::BadRequest().json("Server not found or invalid current players")
    }
}

#[derive(Deserialize)]
pub struct RemoveServerRequest {
    pub game_server_id: String,
    pub private_key: String,
}

pub async fn remove_server(data: web::Data<AppState>, server_info: web::Json<RemoveServerRequest>) -> impl Responder {
    let mut removed = false;
    match data.servers.lock() {
        Ok(mut servers) => {
            if let Some(info) = servers.get(&server_info.game_server_id) {
                if info.private_key == server_info.private_key {
                    servers.remove(&server_info.game_server_id);
                    removed = true;
                } else {
                    return HttpResponse::Unauthorized().json("Invalid private key");
                }
            }
        }
        Err(_) => {
            log::error!("Failed to remove server: {}", server_info.game_server_id);
            return HttpResponse::InternalServerError().json("Failed to remove server");
        }
    };

    if removed {
        log::info!("Server removed: {}", server_info.game_server_id);
        HttpResponse::Ok().json("Server removed")
    } else {
        log::error!("Server not found: {}", server_info.game_server_id);
        HttpResponse::BadRequest().json("Server not found or invalid private key")
    }
}

pub async fn list_servers(data: web::Data<AppState>) -> impl Responder {
    match data.servers.lock() {
        Ok(servers) => {
            let public_servers: Vec<PublicServerInfo> = servers.iter().map(|(id, info)| PublicServerInfo {
                id: id.clone(),
                ip: info.ip.clone(),
                port: info.port,
                server_name: info.server_name.clone(),
                password_protected: info.password_protected,
                game_mode: info.game_mode,
                difficulty: info.difficulty,
                time_passed: info.time_passed.clone(),
                current_players: info.current_players,
                max_players: info.max_players,
                required_mods: info.required_mods.clone(),
                game_version: info.game_version.clone(),
                multiplayer_version: info.multiplayer_version.clone(),
                server_info: info.server_info.clone(),
            }).collect();
            HttpResponse::Ok().json(public_servers)
        }
        Err(_) => HttpResponse::InternalServerError().json("Failed to list servers"),
    }
}
