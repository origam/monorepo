#!/bin/bash
sudo node /root/https-proxy/index.js &
cd /home/origam/HTML5
./startServerTest.sh 
echo "TEST DB Connection"
DATAOUT=$(dotnet origam-utils.dll test-db -a 10 -d 5000 -c "select 1")
#echo "Result IS $DATAOUT !!!!!!!!!!!!!!!!!!" ;
if [[ "$DATAOUT" != True ]]; then
echo "Database connection failed";
exit 1;
fi 
export ASPNETCORE_URLS="http://+:8080"
dotnet Origam.Server.dll &
echo "TEST DB Connection"
DATAOUT=$(dotnet origam-utils.dll test-db -a 5 -d 5000 -c "SELECT 1 FROM dbo.\"OrigamModelVersion\" where \"refSchemaExtensionId\"='${OrigamSettings_SchemaExtensionGuid}'")
if [[ "$DATAOUT" != True ]]; then
echo "Database connection failed";
exit 1;
fi
cd tests_e2e
yarn install --ignore-engines
yarn test:e2e
if [ $? -eq 0 ]
then
  echo "Success."
else
  echo "Scripts failed" >&2
  exit 1
fi
echo "Start workflow integration tests";
cd /home/origam/HTML5_TESTS
cp _OrigamSettings.wf.mssql.template OrigamSettings.config
sed -i "s|OrigamSettings_ModelName|\/home\/origam\/HTML5\/data\/origam${OrigamSettings_ModelSubDirectory}|" OrigamSettings.config
sed -i "s|OrigamSettings_ModelName|\/home\/origam\/HTML5\/data\/origam${OrigamSettings_ModelSubDirectory}|" OrigamSettings.config
sed -i "s/OrigamSettings_DbHost/${OrigamSettings_DbHost}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbHost/${OrigamSettings_DbHost}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbPort/${OrigamSettings_DbPort}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbPort/${OrigamSettings_DbPort}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbUsername/${OrigamSettings_DbUsername}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbUsername/${OrigamSettings_DbUsername}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbPassword/${OrigamSettings_DbPassword}/" OrigamSettings.config
sed -i "s/OrigamSettings_DbPassword/${OrigamSettings_DbPassword}/" OrigamSettings.config
sed -i "s/OrigamSettings_DatabaseName/${DatabaseName}/" OrigamSettings.config
sed -i "s/OrigamSettings_DatabaseName/${DatabaseName}/" OrigamSettings.config
#cat OrigamSettings.config
dotnet test Origam.WorkflowTests.dll
if [ $? -eq 0 ]
then
  echo "Success."
else
  echo "Scripts failed" >&2
  exit 1
fi