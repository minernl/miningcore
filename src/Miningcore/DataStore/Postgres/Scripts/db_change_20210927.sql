ALTER TABLE shares
ADD COLUMN accepted TIMESTAMP NOT NULL;

CREATE INDEX IDX_SHARES_POOL_ACCEPTED ON shares(poolid, accepted);

ALTER TABLE shares 
ADD PRIMARY KEY (poolid, miner, accepted);
