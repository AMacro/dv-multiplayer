use std::sync::{Arc, Mutex};
use crate::server::ServerInfo;

#[derive(Clone)]
pub struct AppState {
    pub servers: Arc<Mutex<std::collections::HashMap<String, ServerInfo>>>,
}
