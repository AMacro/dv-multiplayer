# Derail Valley Lobby Server REST API Documentation

Revision: A
Date: 2024-06-22

## Overview

This document describes the REST API endpoints for the Derail Valley Lobby Server service. The service allows game servers to register, update, and deregister themselves, and provides a list of active servers to clients.
This spec does not provide the server address, as new servers can be created by anyone wishing to host their own lobby server.

## Enums

### Game Modes

The game_mode field in the request body for adding a game server must be one of the following integer values, each representing a specific game mode:

- 0: Career
- 1: Sandbox
- 2: Scenario

### Difficulty Levels

The difficulty field in the request body for adding a game server must be one of the following integer values, each representing a specific difficulty level:

- 0: Standard
- 1: Comfort
- 2: Realistic
- 3: Custom

## Endpoints

### Add Game Server

- **URL:** `/add_game_server`
- **Method:** `POST`
- **Content-Type:** `application/json`
- **Request Body:**
  ```json
  {
      "ip": "string",
      "port": "integer",
	  "server_name": "string",
      "password_protected": "boolean",
	  "game_mode": "integer",
	  "difficulty": "integer",
	  "time_passed": "string",
      "current_players": "integer",
      "max_players": "integer",
	  "required_mods": "string",
	  "game_version": "string",
	  "multiplayer_version": "string",
	  "server_info": "string"
  }
  ```
	- **Fields:**
		- ip (optional string): The IP address of the game server. If not supplied, the requestor's IP shall be used.
		- port (integer): The port number of the game server.
		- server_name (string): The name of the game server (maximum 25 characters).
		- password_protected (boolean): Indicates if the server is password-protected.
		- game_mode (integer): The game mode (see [Game Modes](#game-modes)).
		- difficulty (integer): The difficulty level (see [Difficulty Levels](#difficulty-levels)).
		- time_passed (string): The in-game time passed since the game/session was started.
		- current_players (integer): The current number of players on the server (0 - max_players).
		- max_players (integer): The maximum number of players allowed on the server (>= 1).
		- required_mods (string): The required mods for the server, supplied as a JSON string.
		- game_version (string): The game version the server is running.
		- multiplayer_version (string): The Multiplayer Mod version the server is running.
		- server_info (string): Additional information about the server (maximum 500 characters).
- **Response:**
  - **Success:** 
    - **Code:** 200 OK
    - **Content-Type:** `application/json`
    - **Content:** 
      ```json
	  {
	      "game_server_id": "string",
		  "private_key": "string"
	  }
	  ```
		- game_server_id (string): A GUID assigned to the game server. This GUID uniquely identifies the game server and is used when updating the lobby server.
		- private_key (string): A shared secret between the lobby server and the game server. Must be supplied when updating the lobby server.
  - **Error:**
    - **Code:** 500 Internal Server Error
    - **Content:** `"Failed to add server"`

### Update Server

- **URL:** `/update_game_server`
- **Method:** `POST`
- **Content-Type:** `application/json`
- **Request Body:**
  ```json
  {
      "game_server_id": "string",
	  "private_key": "string",
      "current_players": "integer",
	  "time_passed": "string"
  }
  ```
  	- **Fields:**
		- game_server_id (string): The GUID assigned to the game server (returned from `add_game_server`).
		- private_key (string): The shared secret between the lobby server and the game server (returned from `add_game_server`).
		- current_players (integer): The current number of players on the server (0 - max_players).
		- time_passed (string): The in-game time passed since the game/session was started.
- **Response:**
  - **Success:**
    - **Code:** 200 OK
    - **Content:** `"Server updated"`
  - **Error:**
    - **Code:** 500 Internal Server Error
    - **Content:** `"Failed to update server"`

### Remove Server

- **URL:** `/remove_game_server`
- **Method:** `POST`
- **Content-Type:** `application/json`
- **Request Body:**
  ```json
  {
      "game_server_id": "string",
	  "private_key": "string"
  }
  ```
   	- **Fields:**
		- game_server_id (string): The GUID assigned to the game server (returned from `add_game_server`).
		- private_key (string): The shared secret between the lobby server and the game server (returned from `add_game_server`).
- **Response:**
  - **Success:**
    - **Code:** 200 OK
    - **Content:** `"Server removed"`
  - **Error:**
    - **Code:** 500 Internal Server Error
    - **Content:** `"Failed to remove server"`

### List Game Servers

- **URL:** `/list_game_servers`
- **Method:** `GET`
- **Response:**
  - **Success:**
    - **Code:** 200 OK
    - **Content-Type:** `application/json`
    - **Content:** 
      ```json
      [
          {
			  "ip": "string",
			  "port": "integer",
			  "server_name": "string",
			  "password_protected": "boolean",
			  "game_mode": "integer",
			  "difficulty": "integer",
			  "time_passed": "string"
			  "current_players": "integer",
			  "max_players": "integer",
			  "required_mods": "string",
			  "game_version": "string",
			  "multiplayer_version": "string",
			  "server_info": "string"
          },
          ...
      ]
      ```
  - **Error:**
    - **Code:** 500 Internal Server Error
    - **Content:** `"Failed to retrieve servers"`

## Example Requests

### Add Game Server
Example request:
```bash
curl -X POST -H "Content-Type: application/json" -d '{
    "ip": "127.0.0.1",
    "port": 7777,
	"server_name": "My Derail Valley Server",
    "password_protected": false,
    "current_players": 1,
    "max_players": 10,
	"game_mode": 0,
	"difficulty": 0,
	"time_passed": "0d 10h 45m 12s",
	"required_mods": "",
	"game_version": "98",
	"multiplayer_version": "0.1.0",
	"server_info": "License unlocked server<br>Join our discord and have fun!"
}' http://<lobby-server-address>/add_game_server
```
Example response:
```json
{
    "game_server_id": "0e1759fd-ba6e-4476-ace2-f173af9db342",
    "private_key": "6fca6e1499dab0358f79dc0b251b4e23"
}
```

### Update Game Server
Example request:
```bash
curl -X POST -H "Content-Type: application/json" -d '{
    "game_server_id": "0e1759fd-ba6e-4476-ace2-f173af9db342",
	"private_key": "6fca6e1499dab0358f79dc0b251b4e23",
    "current_players": 2,
    "time_passed": "0d 10h 47m 12s"
}' http://<lobby-server-address>/update_game_server
```
Example response:
```json
{
    "message": "Server updated"
}
```
### Remove Game Server
Example request:
```bash
curl -X POST -H "Content-Type: application/json" -d '{
    "game_server_id": "0e1759fd-ba6e-4476-ace2-f173af9db342",
	"private_key": "6fca6e1499dab0358f79dc0b251b4e23"
}' http://<lobby-server-address>/remove_game_server
```
Example response:
```json
{
    "message": "Server removed"
}
```

### List Game Servers

```bash
curl http://<lobby-server-address>/list_game_servers
```

## Error Handling

In case of an error, the API will return a JSON response with a message indicating the failure.

```json
{
    "error": "string"
}
```

### Common Error Responses

- **500 Internal Server Error**
  - **Content:** `"Failed to add server"`
  - **Content:** `"Failed to update server"`
  - **Content:** `"Failed to remove server"`
  - **Content:** `"Failed to retrieve servers"`
