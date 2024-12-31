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

using System;
using System.Xml;
using System.Xml.XPath;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Origam.Rule;
using Origam.Rule.Xslt;
using Origam.Schema;
using Origam.Schema.WorkflowModel;
using Origam.Service.Core;
using Origam.Workbench.Services;
using System.Xml.Linq;
using System.Windows.Input;

namespace Origam.Workflow.Tasks;
/// <summary>
/// Summary description for ForEachBlockEngineTask.
/// </summary>
public class ForEachBlockEngineTask : BlockEngineTask
{
	private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
	XPathNodeIterator _iter;
	WorkflowEngine _call;
	bool sourceContextChanged;
	public ForEachBlockEngineTask() : base()
	{
	}
	public override void Execute()
	{
		try
		{
			MeasuredExecution();
		}
		catch(Exception ex)
		{
			OnFinished(new WorkflowEngineTaskEventArgs(ex));
		}
	}
	protected override void MeasuredExecution()
	{
		base.MeasuredExecution();
		CleanUp();		
	}
	protected override void OnExecute()
	{
		if (log.IsInfoEnabled)
		{
			log.Info("ForEach Block started.");
		}
		this.Engine.Host.WorkflowFinished += Host_WorkflowFinished;
		ForeachWorkflowBlock block = this.Step as ForeachWorkflowBlock;
		IXmlContainer xmlContainer = GetSourceContextXmlContainer(block);
		XPathNavigator navigator = xmlContainer.Xml.CreateNavigator();
		OrigamXsltContext ctx = OrigamXsltContext.Create(
			new NameTable(), Engine.TransactionId);
		XPathExpression expr = navigator.Compile(block.IteratorXPath);
		expr.SetContext(ctx);
		// code might fail and this handler doesn't get cleared
		// and will interfer with other workflow invocations
		this.Engine.Host.WorkflowMessage += Host_WorkflowMessage;
		_iter = navigator.Select(expr);
		ResumeIteration();
	}
	private void ResumeIteration()
	{
		ForeachWorkflowBlock block = this.Step as ForeachWorkflowBlock;
		_call = this.Engine.GetSubEngine(block, Engine.TransactionBehavior);
		_call.IterationTotal = _iter.Count;
		for (int currentPosition = 1; currentPosition <= _call.IterationTotal;
			currentPosition++)
		{
			if (!block.IgnoreSourceContextChanges && this.sourceContextChanged)
            {
                // reinitialize _iter to updated context store and wind up
                // to current position                    
                IXmlContainer updatedSourceContextStore = GetSourceContextXmlContainer(block);
                XPathNavigator navigator = updatedSourceContextStore.Xml.CreateNavigator();
                OrigamXsltContext ctx = OrigamXsltContext.Create(
                    new NameTable(), Engine.TransactionId);
                XPathExpression expr = navigator.Compile(block.IteratorXPath);
                expr.SetContext(ctx);
                _iter = navigator.Select(expr);
                if (!WindUpTo(currentPosition))
				{
					break;
				}
            }
            else
			{
				bool moved = _iter.MoveNext();
				if (!moved || _iter.CurrentPosition > _iter.Count)
				{
					break;
				}
			}
			// if workflow finished with an exception, we don't proceed
			if(this.Engine == null) return;
			if(log.IsInfoEnabled)
			{
				log.Info("Starting iteration no. " + _iter.CurrentPosition);
			}
			// Set workflow
			_call.ParentContexts.Clear();
			_call.IterationNumber = _iter.CurrentPosition;
			// Fill input context stores
			foreach(Key key in this.Engine.RuleEngine.ContextStoreKeys)
			{
				object context = this.Engine.RuleEngine.GetContext(key);
				if(key.Equals(block.SourceContextStore.PrimaryKey))
				{
					XmlDocument document = XmlTools.GetXmlSlice(_iter); // ((IHasXmlNode)iter.Current).GetNode();
					IDataDocument dataDocument = context as IDataDocument;
					IXmlContainer xmlDocument = context as IXmlContainer;
					if(dataDocument != null)
					{
						// we clone the dataset (no data, just the structure)
						DataSet dataset = dataDocument.DataSet.Clone();
						// we load the iteration data into the dataset
						dataset.ReadXml(new XmlNodeReader(document), XmlReadMode.IgnoreSchema);
						// we add the context into the called engine
						_call.ParentContexts.Add(key, DataDocumentFactory.New(dataset));
					}
					else if(xmlDocument != null)
					{
						_call.ParentContexts.Add(key, new XmlContainer(document));
					}
				}
				else
				{
					// all other contexts
					// pass context directly
					_call.ParentContexts.Add(key, context);
				}
			}
			Engine.Host.ExecuteWorkflow(_call);
		}
	}
    private bool WindUpTo(int currentPosition)
    {
        for (int i = currentPosition; i > 0; i--)
        {
            bool moved = _iter.MoveNext();
            if (!moved || _iter.CurrentPosition > _iter.Count)
            {
				return false;
            }
        }
		return true;
    }
    private IXmlContainer GetSourceContextXmlContainer(ForeachWorkflowBlock block)
	{
		IXmlContainer xmlContainer = this.Engine.RuleEngine.GetContext(
						block.SourceContextStore) as IXmlContainer;
		if (xmlContainer == null)
		{
			throw new ArgumentOutOfRangeException(
				"SourceContextStore",
				block.SourceContextStore,
				ResourceUtils.GetString("ErrorSourceContextNotXmlDocument"));
		}
		return xmlContainer;
	}
	private void CleanUp()
	{
		// there is no other iteration, we finish
		if (this.Engine != null
		) // only if we have not finished already e.g. with an exception
		{
			UnsubscribeEvents();
			OnFinished(new WorkflowEngineTaskEventArgs());
		}
	}
	private void Host_WorkflowFinished(object sender, WorkflowHostEventArgs e)
{
    try
    {
	    if(this.Engine == null) return;	// finished already
	    ForeachWorkflowBlock block = this.Step as ForeachWorkflowBlock;
	    if(e.Engine.WorkflowUniqueId.Equals(_call.WorkflowUniqueId))
	    {
		    if(e.Exception != null)
		    {
			    UnsubscribeEvents();
			    OnFinished(new WorkflowEngineTaskEventArgs(e.Exception));
			    return;
		    }
		    if(!block.IgnoreSourceContextChanges)
		    {
			    // Merge data back after success
			    foreach(DictionaryEntry entry in _call.ParentContexts)
			    {
				    var debugInfo = new Dictionary<string, object>();
				    try
				    {
					    debugInfo["entry.Key"] = entry.Key;
					    if (entry.Key.Equals(
						        block.SourceContextStore.PrimaryKey))
					    {
						    Key castKey = entry.Key as Key;
						    debugInfo["castKey"] = castKey;
						    bool fullMerge =
							    (!entry.Key.Equals(block.SourceContextStore
								    .PrimaryKey));
						    debugInfo["fullMerge"] = fullMerge;
						    object inputContext = _call.RuleEngine.GetContext(castKey);
						    debugInfo["inputContext"] = inputContext;
						    string contextStoreName = this.Engine.ContextStoreName(castKey);
						    debugInfo["contextStoreName"] = contextStoreName;

						    sourceContextChanged = Engine.MergeContext(
							    castKey,
							    inputContext,
							    block,
							    contextStoreName,
							    (fullMerge
								    ? ServiceOutputMethod.FullMerge
								    : ServiceOutputMethod.AppendMergeExisting));
						    //					}
					    }
				    }
				    catch (Exception innerEx)
				    {
					    log.Error("Exception while processing _call.ParentContexts entry.", innerEx);
					    foreach (var valuePair in debugInfo)
					    {
						    log.Error($"{valuePair.Key} is Null: {valuePair.Value == null}");
					    }
					    throw;
				    }

			    }
		    }
		    if(log.IsInfoEnabled)
		    {
			    log.Info("Finishing iteration no. " + _iter.CurrentPosition);
		    }
	    }
    }
    catch (Exception ex)
    {
        // Log all potentially problematic values here for diagnostic purposes
        log.Error("Exception in Host_WorkflowFinished.", ex);

        // Log state of 'this' and related objects
        log.Error($"this: {this}");
        log.Error($"this.Engine is null: {this.Engine == null}");
        log.Error($"this.Step is null: {this.Step == null}");
        log.Error($"this.Step type: {this.Step?.GetType().FullName}");

        // Attempt to log details about 'block'
        ForeachWorkflowBlock block = this.Step as ForeachWorkflowBlock;
        if (block == null)
        {
	        log.Error("Unable to cast this.Step to ForeachWorkflowBlock.");
        }
        else
        {
            log.Error($"block.IgnoreSourceContextChanges: {block.IgnoreSourceContextChanges}");
            log.Error($"block.SourceContextStore is null: {block.SourceContextStore == null}");
            if (block.SourceContextStore != null)
            {
                log.Error($"block.SourceContextStore.PrimaryKey is null: {block.SourceContextStore.PrimaryKey == null}");
            }
        }

        // Log details from 'e' and its engine
        if (e == null)
        {
            log.Error("e (WorkflowHostEventArgs) is null.");
        }
        else
        {
            log.Error($"e.Engine is null: {e.Engine == null}");
            if (e.Engine != null)
            {
                log.Error($"e.Engine.WorkflowUniqueId: {e.Engine.WorkflowUniqueId}");
            }

            log.Error($"e.Exception is null: {e.Exception == null}");
        }

        // Log details about _call
        if (_call == null)
        {
            log.Error("_call is null.");
        }
        else
        {
            log.Error($"_call.WorkflowUniqueId: {_call.WorkflowUniqueId}");
            log.Error($"_call.RuleEngine is null: {_call.RuleEngine == null}");
            log.Error($"_call.DisposeCallStackTraces: {_call.GetDisposeCallStackTraces()}");
            if (_call.ParentContexts != null)
            {
                log.Error($"_call.ParentContexts count: {_call.ParentContexts.Count}");
            }
            else
            {
                log.Error("_call.ParentContexts is null.");
            }
        }

        // Log iteration data if available
        if (_iter == null)
        {
            log.Error("_iter is null.");
        }
        else
        {
            log.Error($"_iter.CurrentPosition: {_iter.CurrentPosition}");
        }
        throw;
    }
}

	private void Host_WorkflowMessage(object sender, WorkflowHostMessageEventArgs e)
	{
		if(e.Engine.WorkflowUniqueId.Equals(_call.WorkflowUniqueId))
		{
			if(e.Exception != null)
			{
				UnsubscribeEvents();
				OnFinished(new WorkflowEngineTaskEventArgs(e.Exception));
			}
		}
	}
	private void UnsubscribeEvents()
	{
		if(this.Engine != null)
		{
			this.Engine.Host.WorkflowFinished -= new WorkflowHostEvent(Host_WorkflowFinished);
			this.Engine.Host.WorkflowMessage -= new WorkflowHostMessageEvent(Host_WorkflowMessage);
		}
	}
}
