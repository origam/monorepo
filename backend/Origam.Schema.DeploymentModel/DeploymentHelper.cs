#region license
/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM (http://www.origam.org).

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with ORIGAM. If not, see <http://www.gnu.org/licenses/>.
*/
#endregion

using Origam.Services;
using Origam.Schema.WorkflowModel;
using Origam.Workbench.Services;
using System;
using Origam.DA.Service;
using Origam.DA;
using static Origam.DA.Common.Enums;

namespace Origam.Schema.DeploymentModel
{
	/// <summary>
	/// Summary description for DeploymentHelper.
	/// </summary>
	public class DeploymentHelper
	{
		public static DeploymentVersion CreateVersion
            (SchemaItemGroup group, string name, string version)
		{
			Package activePackage = ServiceManager.Services
				.GetService<ISchemaService>()
				.ActiveExtension;

			DeploymentVersion deployment = group.NewItem(typeof(DeploymentVersion), 
				activePackage.Id, null) as DeploymentVersion;
			
			deployment.Name = name;
			deployment.VersionString = version;
			deployment.Persist();
			
			return deployment;
		}
		
		public static ServiceCommandUpdateScriptActivity
            CreateDatabaseScript(string name, string script, DatabaseType platformName)
        {
            ISchemaService schema = ServiceManager.Services.GetService(
                typeof(ISchemaService)) as ISchemaService;
            ServiceSchemaItemProvider serviceProvider = schema.GetProvider(
                typeof(ServiceSchemaItemProvider)) as ServiceSchemaItemProvider;
            Origam.Schema.WorkflowModel.Service service = serviceProvider.GetChildByName(
                "DataService", Origam.Schema.WorkflowModel.Service.CategoryConst) as Origam.Schema.WorkflowModel.Service;
            DeploymentSchemaItemProvider deploymentProvider = schema.GetProvider(
                typeof(DeploymentSchemaItemProvider)) as DeploymentSchemaItemProvider;
            DeploymentVersion currentVersion = deploymentProvider.CurrentVersion();
            ServiceCommandUpdateScriptActivity newActivity = currentVersion.NewItem(
                typeof(ServiceCommandUpdateScriptActivity), schema.ActiveSchemaExtensionId, null)
                as ServiceCommandUpdateScriptActivity;
            newActivity.Name += name;
            newActivity.DatabaseType = platformName;
            newActivity.CommandText = script;
            newActivity.Service = service;
            newActivity.Persist();
            return newActivity;
        }

        public static ServiceCommandUpdateScriptActivity CreateSystemRole(string roleName, AbstractSqlDataService abstractSqlData)
        {
            string script = abstractSqlData.CreateSystemRole(roleName);
            ServiceCommandUpdateScriptActivity activity =
                CreateDatabaseScript("AddRole_" + roleName, script,abstractSqlData.PlatformName);
            return activity;
        }
	}
}
