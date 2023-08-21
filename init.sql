CREATE DATABASE IF NOT EXISTS battlebit CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS player
(
    id            INT AUTO_INCREMENT PRIMARY KEY,
    steam_id      BIGINT UNIQUE NOT NULL,
    is_banned     BOOLEAN       NOT NULL,
    roles         INT,
    achievements  BLOB,
    selections    BLOB,
    tool_progress BLOB,
    created_at    TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;


CREATE TABLE IF NOT EXISTS chat_logs
(
    id        INT AUTO_INCREMENT PRIMARY KEY,
    message   TEXT COLLATE utf8mb4_unicode_ci NOT NULL,
    player_id INT                             NOT NULL,
    timestamp DATETIME                        NOT NULL,
    FOREIGN KEY (player_id) REFERENCES player (id)
) CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS player_progress
(
    id                   INT AUTO_INCREMENT PRIMARY KEY,
    player_id            INT,
    kill_count           INT                DEFAULT 0,
    death_count          INT                DEFAULT 0,
    leader_kills         INT                DEFAULT 0,
    assault_kills        INT                DEFAULT 0,
    medic_kills          INT                DEFAULT 0,
    engineer_kills       INT                DEFAULT 0,
    support_kills        INT                DEFAULT 0,
    recon_kills          INT                DEFAULT 0,
    win_count            INT                DEFAULT 0,
    lose_count           INT                DEFAULT 0,
    friendly_shots       INT                DEFAULT 0,
    friendly_kills       INT                DEFAULT 0,
    revived              INT                DEFAULT 0,
    revived_team_mates   INT                DEFAULT 0,
    assists              INT                DEFAULT 0,
    prestige             INT                DEFAULT 0,
    current_rank         INT                DEFAULT 0,
    exp                  INT                DEFAULT 0,
    shots_fired          INT                DEFAULT 0,
    shots_hit            INT                DEFAULT 0,
    headshots            INT                DEFAULT 0,
    completed_objectives INT                DEFAULT 0,
    healed_hps           INT                DEFAULT 0,
    road_kills           INT                DEFAULT 0,
    suicides             INT                DEFAULT 0,
    vehicles_destroyed   INT                DEFAULT 0,
    vehicle_hp_repaired  INT                DEFAULT 0,
    longest_kill         INT                DEFAULT 0,
    play_time_seconds    INT                DEFAULT 0,
    leader_play_time     INT                DEFAULT 0,
    assault_play_time    INT                DEFAULT 0,
    medic_play_time      INT                DEFAULT 0,
    engineer_play_time   INT                DEFAULT 0,
    support_play_time    INT                DEFAULT 0,
    recon_play_time      INT                DEFAULT 0,
    leader_score         INT                DEFAULT 0,
    assault_score        INT                DEFAULT 0,
    medic_score          INT                DEFAULT 0,
    engineer_score       INT                DEFAULT 0,
    support_score        INT                DEFAULT 0,
    recon_score          INT                DEFAULT 0,
    total_score          INT                DEFAULT 0,
    created_at           TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (player_id) REFERENCES player (id)
) CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS admins
(
    id       INT AUTO_INCREMENT PRIMARY KEY,
    name     VARCHAR(255) NOT NULL,
    immunity INT          NOT NULL,
    flags    VARCHAR(255) NOT NULL
) CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS blocks
(
    id              INT AUTO_INCREMENT PRIMARY KEY,
    steam_id        BIGINT                      NOT NULL,
    block_type      ENUM ('BAN', 'GAG', 'MUTE') NOT NULL,
    reason          VARCHAR(255)                NOT NULL,
    expiry_date     DATETIME                    NOT NULL,
    issuer_admin_id INT                         NOT NULL,
    target_ip       VARCHAR(45) DEFAULT NULL,
    admin_ip        VARCHAR(45) DEFAULT NULL,
    FOREIGN KEY (issuer_admin_id) REFERENCES admins (id)
) CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS player_reports
(
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    reporter_id        INT                                                   NOT NULL,
    reported_player_id INT                                                   NOT NULL,
    reason             TEXT COLLATE utf8mb4_unicode_ci                       NOT NULL,
    timestamp          DATETIME                                              NOT NULL,
    status             ENUM ('Pending', 'Reviewed', 'Resolved', 'Dismissed') NOT NULL,
    admin_notes        TEXT COLLATE utf8mb4_unicode_ci,
    FOREIGN KEY (reporter_id) REFERENCES player (id),
    FOREIGN KEY (reported_player_id) REFERENCES player (id)
) CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;
