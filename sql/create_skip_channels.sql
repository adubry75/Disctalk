-- Create skip_channels table
-- This table stores channels that should be skipped during message updates
-- per server (guild).

CREATE TABLE IF NOT EXISTS skip_channels (
    serverId BIGINT NOT NULL,
    channelId BIGINT NOT NULL,
    reason VARCHAR(255),
    PRIMARY KEY (serverId, channelId),
    INDEX idx_serverId (serverId)
);
