<?php
include 'DatabaseInterface.php';

class MySQLDatabase implements DatabaseInterface {
    private $pdo;

    public function __construct($dbConfig) {
        $this->pdo = new PDO("mysql:host={$dbConfig['host']};dbname={$dbConfig['dbname']}", $dbConfig['username'], $dbConfig['password']);
        $this->pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
    }

    public function addGameServer($data) {
        $stmt = $this->pdo->prepare("INSERT INTO game_servers (game_server_id, private_key, ip, port, server_name, password_protected, game_mode, difficulty, time_passed, current_players, max_players, required_mods, game_version, multiplayer_version, server_info, last_update) 
                                     VALUES (:game_server_id, :private_key, :ip, :port, :server_name, :password_protected, :game_mode, :difficulty, :time_passed, :current_players, :max_players, :required_mods, :game_version, :multiplayer_version, :server_info, :last_update)");
        $stmt->execute([
            ':game_server_id' => $data['game_server_id'],
            ':private_key' => $data['private_key'],
            ':ip' => $data['ip'],
            ':port' => $data['port'],
            ':server_name' => $data['server_name'],
            ':password_protected' => $data['password_protected'],
            ':game_mode' => $data['game_mode'],
            ':difficulty' => $data['difficulty'],
            ':time_passed' => $data['time_passed'],
            ':current_players' => $data['current_players'],
            ':max_players' => $data['max_players'],
            ':required_mods' => $data['required_mods'],
            ':game_version' => $data['game_version'],
            ':multiplayer_version' => $data['multiplayer_version'],
            ':server_info' => $data['server_info'],
            ':last_update' => time() //use current time
        ]);
        return json_encode([
            "game_server_id" => $data['game_server_id'],
            "private_key" => $data['private_key']
        ]);
    }

    public function updateGameServer($data) {
        $stmt = $this->pdo->prepare("UPDATE game_servers 
                                     SET current_players = :current_players, time_passed = :time_passed, last_update = :last_update
                                     WHERE game_server_id = :game_server_id");
        $stmt->execute([
            ':current_players' => $data['current_players'],
            ':time_passed' => $data['time_passed'],
            ':last_update' => time(), // Update with current time
            ':game_server_id' => $data['game_server_id']
        ]);
    
        return $stmt->rowCount() > 0 ? json_encode(["message" => "Server updated"]) : json_encode(["error" => "Failed to update server"]);
    }

    public function removeGameServer($data) {
        $stmt = $this->pdo->prepare("DELETE FROM game_servers WHERE game_server_id = :game_server_id");
        $stmt->execute([':game_server_id' => $data['game_server_id']]);
        return $stmt->rowCount() > 0 ? json_encode(["message" => "Server removed"]) : json_encode(["error" => "Failed to remove server"]);
    }
    
    public function listGameServers() {
        // Remove servers that exceed TIMEOUT directly in the SQL query
        $stmt = $this->pdo->prepare("DELETE FROM game_servers WHERE last_update < :timeout");
        $stmt->execute([':timeout' => time() - TIMEOUT]);
    
        // Fetch remaining servers
        $stmt = $this->pdo->query("SELECT * FROM game_servers");
        $servers = $stmt->fetchAll(PDO::FETCH_ASSOC);
    
        return json_encode($servers);
    }

    public function getGameServer($game_server_id) {
        $stmt = $this->pdo->prepare("SELECT * FROM game_servers WHERE game_server_id = :game_server_id");
        $stmt->execute([':game_server_id' => $game_server_id]);
        return json_encode($stmt->fetch(PDO::FETCH_ASSOC));
    }
}

?>
