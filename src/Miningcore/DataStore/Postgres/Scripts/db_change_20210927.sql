ALTER TABLE shares
ADD COLUMN accepted TIMESTAMP NOT NULL;

CREATE INDEX IDX_SHARES_POOL_ACCEPTED ON shares(poolid, accepted);

ALTER TABLE shares 
ADD PRIMARY KEY (poolid, miner, accepted);

ALTER TABLE shares
ADD COLUMN processed TIMESTAMP NULL;

CREATE INDEX IDX_SHARES_POOL_PROCESSED_ACCEPTED ON shares(poolid, processed, accepted);
