sudo apt-get update -y && sudo apt-get install -y --no-install-recommends cmake build-essential libssl-dev libsodium-dev pkg-config libboost-all-dev libzmq5 && \
cd $1/src/Native/libmultihash && make clean && make && yes| cp -rf libmultihash.so $2 && \
cd $1/src/Native/libcryptonight && make clean && make && yes| cp -rf libcryptonight.so $2 && \
cd $1/src/Native/libcryptonote && make clean && make && yes| cp -rf libcryptonote.so $2 && \
cd $1/src && dotnet build --configuration $3 --framework net5.0  --output $2