# Lobby Server - PHP

This is a PHP implementation of the Derail Valley Lobby Server REST API service. It is designed to run on any standard web hosting and does not rely on long-running/persistent behaviour.
HTTPS support depends on the configuration of the hosting environment.

As this implementation is not persistent in memory, a database is used to store server information. Two options are available for the database engine - a JSON based flatfile or a MySQL database.

## Installing

The following instructions assume you will be using an Apache web server and may need to be modified for other configurations.

1. Copy the following files to your public html folder (consult your web server/web host's documentation)
```
index.php
DatabaseInterface.php
FlatfileDatabase.php
MySQLDatabase.php
.htaccess
```
2. Copy `config.php` to a secure location outside of your public html directory
3. Edit `index.php` and update the path to the config file on line 2:
```php
<?php
include '/path/to/config.php';
...
```

### Setup the database

Choose between a MySQL database and a flatfile database.

#### MySQL

1. Edit `config.php` and set the `type` parameter to `mysql`
2. Update the `host`, `dbname`, `username` and `password` parameters for your MySQL server 
3. Copy the `install.php` file to a location you can execute it
4. Edit `install.php` and update the path to the config file on line 2:
```php
<?php
include '/path/to/config.php';
...
```
5. Run `install.php`
6. If `install.php` is in a publically visible location, remove it


#### Flatfile
1. In a secure location (outside of your public html directory), create a directory to store the database
2. Edit `config.php` and set the `type` parameter to `flatfile`
3. Update `flatfile_path` to the directory created in step 1


## Configuration Parameters
The server can be configured using the `config.php` file.

Below are the available parameters along with their defaults:
-   `TIMEOUT` (u64): The time-out period in seconds for server removal.
	-- Default: `120`
--   `dbConfig`.`type` (string): Type of database for persisting data.
	-- Options: `mysql`, `flatfile`
	-- Default: `mysql`
-   `dbConfig`.`host` (string): hostname of MySQLServer.
	-- Required when: `dbConfig`.`type` is `mysql`
	-- Default: `localhost`
-   `dbConfig`.`dbname` (string): Database name.
  - Required when: `dbConfig`.`type` is `mysql`
	-- Default: N/A
-   `dbConfig`.`username` (string): Username for connecting to the MySQLServer.
	-- Required when: `dbConfig`.`type` is `mysql`
	-- Default: N/A
-   `dbConfig`.`password` (string): Password for connecting to the MySQLServer.
	-- Required when: `dbConfig`.`type` is `mysql`
	-- Default: N/A
-   `dbConfig`.`flatfile_path` (string): Path to secure directory.
	-- Required when: `dbConfig`.`type` is `flatfile`
	-- Default: N/A

Example `config.php` using MySQL:
```php
<?php

// Timeout value (in seconds)
define('TIMEOUT', 120);

// Database configuration
$dbConfig = [
    'type' => 'mysql',
    'host' => 'localhost',
    'dbname' => 'dv_lobby',
    'username' => 'dv_lobby_server',
    'password' => 'n16O5+LMpeqI`{E',
    'flatfile_path' => '' // Path to store the flatfile database
];
?>
```

Example `config.php` using Flatfile:
```php
<?php

// Timeout value (in seconds)
define('TIMEOUT', 120);

// Database configuration
$dbConfig = [
    'type' => 'flatfile',
    'host' => '',
    'dbname' => '',
    'username' => '',
    'password' => '',
    'flatfile_path' => '/dv_lobby/flatfile.db' // Path to store the flatfile database
];
?>
```

## Security Considerations
This is a non-comprehensive overview of security considerations. You should always use up-to-date best practices and seek professional advice where required.

### Environment variables
Consider using environment variables to store sensitive database credentials (e.g. `dbConfig`.`host`, `dbConfig`.`dbname`, `dbConfig`.`username`, `dbConfig`.`password`) instead of hardcoding them in config.php.
Your `config.php` can be updated to reference the environment variables.

Example:
```php
$dbConfig = [
    'type' => 'mysql',
    'host' => getenv('DB_HOST'),
    'dbname' => getenv('DB_NAME'),
    'username' => getenv('DB_USER'),
    'password' => getenv('DB_PASSWORD'),
    'flatfile_path' => '/path/to/flatfile.db'
];
```


### File Permissions
Ensure that `config.php` and any other sensitive files outside the web root are only readable by the web server user (chmod 600).
For directories containing flatfile databases, restrict permissions (chmod 700 or 750) to prevent unauthorised access.

### HTTPS (SSL)
Configure your server to use https. Many web hosts provide free SSL certificates via Let's Encrypt.
Consider forcing https via server config/`.httaccess`.

Example:
```apacheconf
RewriteEngine On 
RewriteCond %{HTTPS} off 
RewriteRule ^ https://%{HTTP_HOST}%{REQUEST_URI} [L,R=301]
```
