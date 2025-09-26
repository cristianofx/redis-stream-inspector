$rid   = "win-x64"
$conf  = "Release"
$props_single = "/p:PublishSingleFile=true /p:PublishTrimmed=false"
$props_multi = "/p:PublishSingleFile=false /p:PublishTrimmed=false"

dotnet publish src/RedisInspector.CLI/RedisInspector.CLI.csproj -c $conf -r $rid --self-contained true $props_single
dotnet publish src/RedisInspector.UI/RedisInspector.UI.csproj  -c $conf -r $rid --self-contained true $props
