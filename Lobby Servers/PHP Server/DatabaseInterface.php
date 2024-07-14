<?php

interface DatabaseInterface {
    public function addGameServer($data);
    public function updateGameServer($data);
    public function removeGameServer($data);
    public function listGameServers();
}

?>
