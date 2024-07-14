<?php
include 'config.php';

// Check if the database type is MySQL
if ($dbConfig['type'] !== 'mysql') {
    die("This install script only supports MySQL databases.");
}

try {
    // Connect to the MySQL server
    $dsn = 'mysql:host=' . $dbConfig['host'];
    $pdo = new PDO($dsn, $dbConfig['username'], $dbConfig['password']);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

    // Create the database if it doesn't exist
    $sql = "CREATE DATABASE IF NOT EXISTS " . $dbConfig['dbname'];
    $pdo->exec($sql);
    echo "Database created successfully.<br>";

    // Connect to the newly created database
    $dsn = 'mysql:host=' . $dbConfig['host'] . ';dbname=' . $dbConfig['dbname'];
    $pdo = new PDO($dsn, $dbConfig['username'], $dbConfig['password']);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

    // Create the game_servers table
    $sql = "
    CREATE TABLE IF NOT EXISTS game_servers (
        game_server_id VARCHAR(50) PRIMARY KEY,
        private_key VARCHAR(255) NOT NULL,
        ip VARCHAR(45) NOT NULL,
        port INT NOT NULL,
        server_name VARCHAR(100) NOT NULL,
        password_protected BOOLEAN NOT NULL,
        game_mode VARCHAR(50) NOT NULL,
        difficulty VARCHAR(50) NOT NULL,
        time_passed INT NOT NULL,
        current_players INT NOT NULL,
        max_players INT NOT NULL,
        required_mods TEXT NOT NULL,
        game_version VARCHAR(50) NOT NULL,
        multiplayer_version VARCHAR(50) NOT NULL,
        server_info TEXT NOT NULL,
        last_update INT NOT NULL
    );
    ";

    // Execute the SQL to create the table
    $pdo->exec($sql);
    echo "Table 'game_servers' created successfully.<br>";

} catch (PDOException $e) {
    die("DB ERROR: " . $e->getMessage());
}
?>
