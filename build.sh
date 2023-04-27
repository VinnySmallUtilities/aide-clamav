rm -rf ./publish
mkdir  ./publish

rm -rf ./publish/build.net
rm -rf ./publish/build.cur
rm -rf ./publish/build.lin64
rm -rf ./publish/build.lin64sc

dotnet publish --output ./publish/build.net -c Release --self-contained false /p:PublishSingleFile=false

dotnet publish --output ./publish/build.cur -c Release --use-current-runtime true --self-contained false /p:PublishSingleFile=true /p:PublishReadyToRun=true

dotnet publish --output ./publish/build.lin64   -c Release -r linux-x64 --self-contained false /p:PublishSingleFile=true
dotnet publish --output ./publish/build.lin64sc -c Release -r linux-x64 --self-contained true  /p:PublishSingleFile=true /p:PublishTrimmed=true


7z a -y -t7z -stl -m0=lzma -mx=9 -ms=on -bb0 -bd -ssc -ssw ./publish/aide-clamav-dotnet.7z  ./publish/build.net/     >> /dev/null
7z a -y -t7z -stl -m0=lzma -mx=9 -ms=on -bb0 -bd -ssc -ssw ./publish/aide-clamav-lin64.7z   ./publish/build.lin64/   >> /dev/null
7z a -y -t7z -stl -m0=lzma -mx=9 -ms=on -bb0 -bd -ssc -ssw ./publish/aide-clamav-lin64sc.7z ./publish/build.lin64sc/ >> /dev/null

echo
echo 'Published in '
echo `realpath ./publish`
echo
echo 'aide-clamav-dotnet  for execute with dotnet aide-clamav.dll'
echo 'aide-clamav-lin64   for execute aide-clamav (with .NET 7.0 on Linux)'
echo 'aide-clamav-lin64sc for execute aide-clamav on Linux x64 without .NET 7.0'

cp -fvu ./publish/build.lin64sc/aide-clamav .

