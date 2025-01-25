<?php
include '/home/andrewcr/private/derailvalley/config.php';

// Determine the database type and include the appropriate module
switch ($dbConfig['type']) {
    case 'mysql':
        include 'MySQLDatabase.php';
        $db = new MySQLDatabase($dbConfig);
        break;
    case 'flatfile':
        include 'FlatfileDatabase.php';
        $db = new FlatfileDatabase($dbConfig);
        break;
    default:
        die('Unsupported database type');
}

// Define routes
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $data = json_decode(file_get_contents('php://input'), true);

    switch ($_SERVER['REQUEST_URI']) {
        case '/add_game_server':
            echo add_game_server($db, $data);
            break;
        case '/update_game_server':
            echo update_game_server($db, $data);
            break;
        case '/remove_game_server':
            echo remove_game_server($db, $data);
            break;
        default:
            http_response_code(404);
            break;
    }

} elseif ($_SERVER['REQUEST_METHOD'] === 'GET') {
    if ($_SERVER['REQUEST_URI'] === '/list_game_servers') {
        echo list_game_servers($db);
    } else {
        http_response_code(404);
    }
} else {
    http_response_code(405); // Method Not Allowed
}

function add_game_server($db, $data) {
    if (!validate_server_info($data)) {
        http_response_code(500);
        return json_encode(["error" => "Invalid server information"]);
    }

    $data['game_server_id'] = uuid_create();
    $data['private_key'] = generate_private_key();
    
    return $db->addGameServer($data);
}

function update_game_server($db, $data) {
    if (!validate_server_update($db, $data)) {
        http_response_code(500);
        return json_encode(["error" => "Invalid game server ID or private key"]);
    }

    $data['last_update'] = time();
    return $db->updateGameServer($data);
}

function remove_game_server($db, $data) {
    if (!validate_server_update($db, $data)) {
        return json_encode(["error" => "Invalid game server ID or private key"]);
    }

    return $db->removeGameServer($data);
}

function list_game_servers($db) {
    $servers = json_decode($db->listGameServers(), true);

    // Remove private keys from the servers before returning
    // and select the correct protocol version for the requestor
    foreach ($servers as &$server) {
        unset($server['private_key']);
        unset($server['last_update']);

        if(!isset($server['ipv4'])){
            $server['ipv4'] = '';
        }

        if(!isset($server['ipv6'])){
            $server['ipv6'] = '';
        }

        if(filter_var($_SERVER['REMOTE_ADDR'], FILTER_VALIDATE_IP, FILTER_FLAG_IPV4)){
            //Host made a request on IPv4, remove IPv6 address as we assume they don't support it.
            unset($server['ipv6']);

        }
    }
    return json_encode($servers);
}

function validate_server_info($data) {

    if(!isset($data['ipv4']) || !filter_var($data['ipv4'], FILTER_VALIDATE_IP, FILTER_FLAG_IPV4)){
        $data['ipv4'] = '';
    }elseif(!isset($data['ipv6']) || !filter_var($data['ipv6'], FILTER_VALIDATE_IP, FILTER_FLAG_IPV6)){
        $data['ipv6'] = '';
    }

    if (
            //make sure we have at least one IP
            $data['ipv4'] == '' && $data['ipv6'] == '' ||   

            //Make sure we have all required fields
            !isset($data['server_name']) ||
            !isset($data['server_info']) ||
            !isset($data['current_players']) ||
            !isset($data['max_players']) ||

            //Validate fields
            strlen($data['server_name']) > 25 ||
            strlen($data['server_info']) > 500 ||
            $data['current_players'] > $data['max_players'] ||
            $data['max_players'] < 1
        ){

        return false;
    }

    return true;
}

function validate_server_update($db, $data) {
    $server = json_decode($db->getGameServer($data['game_server_id']), true);
    return $server && $server['private_key'] === $data['private_key'];
}

function uuid_create() {
    return sprintf('%04x%04x-%04x-%04x-%04x-%04x%04x%04x',
        mt_rand(0, 0xffff), mt_rand(0, 0xffff), mt_rand(0, 0xffff),
        mt_rand(0, 0x0fff) | 0x4000,
        mt_rand(0, 0x3fff) | 0x8000,
        mt_rand(0, 0xffff), mt_rand(0, 0xffff), mt_rand(0, 0xffff)
    );
}

function generate_private_key() {
    // Generate a 128-bit (16 bytes) random binary string
    $random_bytes = random_bytes(16);
    
    // Convert the binary string to a hexadecimal representation
    $private_key = bin2hex($random_bytes);
    
    return $private_key;
}

?>