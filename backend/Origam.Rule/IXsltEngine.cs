﻿#region license
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

using System;
using System.Xml;
using System.Collections;
using Origam.Schema.EntityModel;
using Origam.DA.ObjectPersistence;
using System.Xml.XPath;
using System.IO;
using Origam.ServiceCore;

namespace Origam.Rule
{
    /// <summary>
    /// Summary description for AsXslTransform.
    /// </summary>
    public interface IXsltEngine : ITracable
    {
        IPersistenceProvider PersistenceProvider {get; set;}
        string TraceStepName {get; set;}
        Guid TraceStepId {get; set;}
        Guid TraceWorkflowId {get; set;}
        bool Trace { get; set; }

        IXmlContainer Transform(IXmlContainer data, Guid transformationId, Hashtable parameters, RuleEngine ruleEngine, IDataStructure outputStructure, bool validateOnly);
        IXmlContainer Transform(IXmlContainer data, Guid transformationId, Guid retransformationId, Hashtable parameters, Hashtable retransformationParameters, RuleEngine ruleEngine, IDataStructure outputStructure, bool validateOnly);
        IXmlContainer Transform(IXmlContainer data, string xsl, Hashtable parameters, RuleEngine ruleEngine, IDataStructure outputStructure, bool validateOnly);
        void Transform(
            IXPathNavigable input, Guid transformationId, Hashtable parameters, 
            RuleEngine ruleEngine, Stream output);
    }
}
