<?php
include 'DatabaseInterface.php';

class FlatfileDatabase implements DatabaseInterface {
    private $filePath;

    public function __construct($dbConfig) {
        $this->filePath = $dbConfig['flatfile_path'];
    }

    private function readData() {
        if (!file_exists($this->filePath)) {
            return [];
        }
        return json_decode(file_get_contents($this->filePath), true) ?? [];
    }

    private function writeData($data) {
        file_put_contents($this->filePath, json_encode($data, JSON_PRETTY_PRINT));
    }

    public function addGameServer($data) {
        $data['last_update'] = time(); // Set current time as last_update
    
        $servers = $this->readData();
        $servers[] = $data;
        $this->writeData($servers);
        
        return json_encode([
            "game_server_id" => $data['game_server_id'],
            "private_key" => $data['private_key']
        ]);
    }

    public function updateGameServer($data) {
        $servers = $this->readData();
        $updated = false;
    
        foreach ($servers as &$server) {
            if ($server['game_server_id'] === $data['game_server_id']) {
                $server['current_players'] = $data['current_players'];
                $server['time_passed'] = $data['time_passed'];
                $server['last_update'] = time(); // Update with current time
                $updated = true;
                break;
            }
        }
    
        if ($updated) {
            $this->writeData($servers);
            return json_encode(["message" => "Server updated"]);
        } else {
            return json_encode(["error" => "Failed to update server"]);
        }
    }

    public function removeGameServer($data) {
        $servers = $this->readData();
        $servers = array_filter($servers, function($server) use ($data) {
            return $server['game_server_id'] !== $data['game_server_id'];
        });
        $this->writeData(array_values($servers));
        return json_encode(["message" => "Server removed"]);
    }

    public function listGameServers() {
        $servers = $this->readData();
        $current_time = time();
        $active_servers = [];
        $changed = false;
    
        foreach ($servers as $key => $server) {
            if ($current_time - $server['last_update'] <= TIMEOUT) {
                $active_servers[] = $server;
            } else {
                $changed = true; // Indicates there's a change if any server is removed
            }
        }
    
        if ($changed) {
            $this->writeData($active_servers); // Write back only if there are changes
        }
    
        return json_encode($active_servers);
    }
    
    

    public function getGameServer($game_server_id) {
        $servers = $this->readData();
        foreach ($servers as $server) {
            if ($server['game_server_id'] === $game_server_id) {
                return json_encode($server);
            }
        }
        return json_encode(null);
    }
}

?>
