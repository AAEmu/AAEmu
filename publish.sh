VERSION_PREFIX=0.0.2.0
VERSION_SUFFIX=alpha

FRAMEWORK=netcoreapp2.2

CONFIGURATION=Debug
#CONFIGURATION=Release

mkdir -p publish;
mkdir -p publish/$CONFIGURATION;

for runtime in "win7-x64" "win7-x86" "win8-x64" "win8-x86" "win81-x64" "win81-x86" "win10-x64" "win10-x86" "centos.7-x64" "debian.9-x64" "ubuntu.18.04-x64" "sles-x64" "sles.12-x64" "sles.12.1-x64" "sles.12.2-x64" "sles.12.3-x64" "alpine-x64" "alpine.3.7-x64"; do
	dotnet publish -c $CONFIGURATION -r $runtime --self-contained true;
	
	mkdir -p publish/$CONFIGURATION/$runtime;
	
	for project in "AAEmu.Login" "AAEmu.Game"; do
		mkdir -p publish/$CONFIGURATION/$runtime/$project;
		mv $project/bin/$CONFIGURATION/$FRAMEWORK/$runtime/publish/* publish/$CONFIGURATION/$runtime/$project;
		rm -R $project/bin/$CONFIGURATION/$FRAMEWORK/$runtime;
	done;
	
	cd publish/$CONFIGURATION/$runtime;
	zip -r ../../../publish/$CONFIGURATION/AAEmu.$VERSION_PREFIX-$VERSION_SUFFIX+$runtime.zip *;
	cd ../../../;
	
	rm -R publish/$CONFIGURATION/$runtime;
done;