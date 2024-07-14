<?php

// Timeout value (in seconds)
define('TIMEOUT', 120);

// Database configuration
$dbConfig = [
    'type' => 'mysql', // Change to 'flatfile' to use flatfile database
    'host' => 'localhost',
    'dbname' => 'your_database',
    'username' => 'your_username',
    'password' => 'your_password',
    'flatfile_path' => '/path/to/flatfile.db' // Path to store the flatfile database
];

?>