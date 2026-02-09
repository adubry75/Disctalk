-- Seed skip_channels table with initial data
-- Replace SERVER_ID_HERE with your actual server ID before running this script.
-- These are channels that are inaccessible or were discontinued.

-- Usage:
--   1. Replace all instances of SERVER_ID_HERE with your actual Discord server ID
--   2. Run this script against your database
--   3. Verify with: SELECT * FROM skip_channels WHERE serverId = YOUR_SERVER_ID;

INSERT INTO skip_channels (serverId, channelId, reason) VALUES
(SERVER_ID_HERE, 374631528275378176, 'Voice Channels'),
(SERVER_ID_HERE, 374631528275378177, 'General (not general)'),
(SERVER_ID_HERE, 374633617588224001, 'Topics'),
(SERVER_ID_HERE, 374677953004437514, 'Gaming (not gaming)'),
(SERVER_ID_HERE, 428448605762748426, 'hall of justice'),
(SERVER_ID_HERE, 628435158902636586, 'craigslist'),
(SERVER_ID_HERE, 747310713403342869, 'movie night'),
(SERVER_ID_HERE, 748691464481144952, 'bot log'),
(SERVER_ID_HERE, 798357201605099570, 'pinterest'),
(SERVER_ID_HERE, 882133723892568076, 'Music'),
(SERVER_ID_HERE, 882789707468136458, 'theta'),
(SERVER_ID_HERE, 882789786560102490, 'coastal town'),
(SERVER_ID_HERE, 964682118884110346, 'the forgotten one eye'),
(SERVER_ID_HERE, 991863219008307200, 'Main'),
(SERVER_ID_HERE, 991948107954786365, 'Admin'),
(SERVER_ID_HERE, 1021969695433314314, 'void'),
(SERVER_ID_HERE, 1187489188417917029, 'meetups'),
(SERVER_ID_HERE, 1228558055248232558, 'automod log')
ON DUPLICATE KEY UPDATE reason = VALUES(reason);
