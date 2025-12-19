#some notes:
# local mode is the easiest to capture just the game traffic
# allow hosts filters the traffic to just the grpc stuff which is what we actually care about
#the script is the most important part, it reads a descriptor set file output by our CLI tool, 
# then uses it to decide which requests to stream, displays them nicely in the UI, and writes them to disk

#mitmweb.exe --mode local:StarCitizen.exe  --allow-hosts 'cloudimperiumgames.com' -s stream-sc.py --set descriptor="D:\\repos\\starcitizen\\StarBreaker-master\\scripts\\set.bin"
uvx --from mitmproxy --with protobuf mitmweb --mode local:StarCitizen.exe  --allow-hosts 'cloudimperiumgames.com' -s stream-sc.py --set descriptor="set.bin"



# A exécuter à chaquye version de SC :
# .\StarBreaker.Cli.exe proto-set-extract -i 'D:\Games\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe' -o ..\..\..\..\StarBreaker.Grpc\set.bin
# .\StarBreaker.Cli.exe dcb-generate -o ..\..\..\..\StarBreaker.DataCore.Generated\Generated -p 'D:\Games\Roberts Space Industries\StarCitizen\LIVE\Data.p4k'
# .\StarBreaker.Cli.exe proto-extract -i 'D:\Games\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe' -o ..\..\..\..\StarBreaker.Grpc\Protos
# Mise à jour pour mitm
# .\StarBreaker.Cli.exe proto-set-extract -i 'D:\Games\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe' -o ..\..\..\..\..\scripts\set.bin