CREATE DATABASE IF NOT EXISTS game CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE game;

CREATE TABLE IF NOT EXISTS rooms (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name VARCHAR(100) NOT NULL,
  connection_type VARCHAR(32) NOT NULL DEFAULT 'local',
  connection_value VARCHAR(255) NOT NULL,
  map_id VARCHAR(64) NULL,
  is_public BOOLEAN NOT NULL DEFAULT TRUE,
  max_players INT UNSIGNED NOT NULL DEFAULT 4,
  current_players INT UNSIGNED NOT NULL DEFAULT 1,
  status ENUM('open', 'closed', 'in_game') NOT NULL DEFAULT 'open',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  INDEX idx_rooms_public_status_created_at (is_public, status, created_at),
  INDEX idx_rooms_status_created_at (status, created_at)
);
