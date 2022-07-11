const fs = require("fs");

const pluginRegistrationFilePath = "src/plugins/tools/PluginRegistration.ts"
const envFilePath = ".env.production.local";

// for debugging:
// const pluginRegistrationFilePath = "../src/plugins/tools/PluginRegistration.ts"
// const envFilePath = "../.env.production.local";

const pluginVersionsVariableName = "REACT_APP_ORIGAM_UI_PLUGINS";

function getPackageName(pluginName, registrationFile) {
  const pluginPackageRegEx = new RegExp('import\\s*{\\s*' + pluginName + '\\s*}\\s*from\\s*"([/@\\w/\\-]+)";', 'g');
  return pluginPackageRegEx.exec(registrationFile)[1];
}

function findUsedPackageNames(registrationFile) {
  const pluginRegEx = /registerPlugin\s*\(\s*"(\w+)",/g;
  return [...registrationFile.matchAll(pluginRegEx)].map(x => x[1]);
}

function setVariableValue(variableName, value, envFile) {
  if (envFile.includes(variableName)) {
    const regEx = new RegExp("(" + variableName + "\\s*=\\s*)(.*)", 'g');
    return envFile.replace(regEx, "$1" + value);
  } else {
    return `${envFile}\n${variableName} = ${value}`;
  }
}

if (!fs.existsSync(pluginRegistrationFilePath)) {
  throw new Error(
    `File '${pluginRegistrationFilePath}' was not found.`
  );
}

const registrationFile = fs.readFileSync(pluginRegistrationFilePath).toString();
const usedPluginNames = findUsedPackageNames(registrationFile)
const packageNamesAndVersions = usedPluginNames
  .map(pluginName => {
    const packageName = getPackageName(pluginName, registrationFile);
    const packageReference = require(`../node_modules/${packageName}/package.json`);
    return `${packageName}: ${packageReference.version}`;
  })
  .join(";")


let newEnvFile;
if (fs.existsSync(envFilePath)) {
  const envFile = fs.readFileSync(envFilePath).toString();

  newEnvFile = setVariableValue(pluginVersionsVariableName, packageNamesAndVersions, envFile)
} else {
  newEnvFile = `${pluginVersionsVariableName} = ${packageNamesAndVersions}\n`;
}

fs.writeFileSync(envFilePath, newEnvFile);
