echo "**********************************************************************************"
echo "Use `build.sh -all` to build C++ Libraries"
echo "**********************************************************************************"

echo "========================== STOP SERVICE ==========================================" && \
/etc/init.d/poolservice stop && \
sleep 3 && \
echo "========================== PULL LATEST CODE ======================================" && \
cd ~/miningcore && \
git pull && \
echo "========================== CLEAN OLD BUILD =======================================" && \
rm -d -R -f build
mkdir build
echo "Done"

if [ "$1" = "-all" ]; then 
echo "========================== BUILD LIB =============================================" && \
	cd ~/miningcore/src/Native/libmultihash && \
	make clean && make && \
	cp libmultihash.so ../../../build/
	cd ~/miningcore/src/Native/libcryptonight && \
	make clean && make && \
	cp libcryptonight.so ../../../build/
	cd ~/miningcore/src/Native/libcryptonote && \
	make clean && make && \
	cp libcryptonote.so ../../../build/
fi
 
echo "========================== BUILD SRC =============================================" && \
cd ~/miningcore/src/ && \
dotnet publish -c Release --framework net5.0  -o ../build  && \
echo "========================== START SERVICE =========================================" && \
cd ~/miningcore/build && \
/etc/init.d/poolservice start && \
echo "=========================== START LOG ============================================" && \
tail -f ~/pooldata/logs/core.log