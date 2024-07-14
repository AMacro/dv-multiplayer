use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Clone)]
pub struct ServerInfo {
    pub ip: String,
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
    #[serde(skip_serializing)]
    pub last_update: u64,
    #[serde(skip_serializing)]
    pub private_key: String,
}

#[derive(Serialize, Deserialize, Clone)]
pub struct PublicServerInfo {
    pub id: String,
    pub ip: String,
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

#[derive(Serialize, Deserialize, Clone)]
pub struct AddServerResponse {
    pub game_server_id: String,
    pub private_key: String,
}

pub fn validate_server_info(info: &ServerInfo) -> Result<(), &'static str> {
    if info.server_name.len() > 25 {
        return Err("Server name exceeds 25 characters");
    }
    if info.server_info.len() > 500 {
        return Err("Server info exceeds 500 characters");
    }
    if info.current_players > info.max_players {
        return Err("Current players exceed max players");
    }
    if info.max_players < 1 {
        return Err("Max players must be at least 1");
    }
    Ok(())
}
