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
using System.IO;
using System.Text;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Origam.DA;
using Origam.DA.Service;
using Origam.DA.ObjectPersistence;
using Origam.Schema;
using Origam.Schema.EntityModel;
using Origam.Schema.WorkflowModel;
using Origam.Workbench.Services;
using Origam.Rule;
using Origam.Schema.RuleModel;
using log4net;
using System.Linq;
using Origam.Extensions;
using Origam.Schema.EntityModel.Interfaces;
using Origam.Service.Core;

namespace Origam.Workflow;

public class WorkflowEngine : IDisposable
{
	private static readonly ILog log = LogManager.GetLogger(
		System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);
	private DatasetGenerator datasetGenerator 
		= new(userDefinedParameters:true);
	private ITracingService tracingService 
		= ServiceManager.Services.GetService<ITracingService>();
	private IParameterService parameterService 
		= ServiceManager.Services.GetService<IParameterService>();
	private readonly WorkflowStackTrace workflowStackTrace = new();
	public bool Trace { get; set; } = false;
	private readonly OperationTimer localOperationTimer = new();
	private Exception caughtException;
	private List<string> disposeCallStackTraces = new List<string>();
	private object lockObject = new object();

	public string GetDisposeCallStackTraces()
	{
		lock (lockObject)
		{
			return string.Join("\n", disposeCallStackTraces);
		}
	}

	public WorkflowEngine(string transactionId = null)
	{
		WorkflowUniqueId = Guid.NewGuid();
		WorkflowInstanceId = WorkflowUniqueId;
		this.transactionId = transactionId;
	}

	#region Properties
	private readonly Dictionary<Key, WorkflowStepResult> taskResults = new();
	public Dictionary<Key, WorkflowStepResult> TaskResults => taskResults;

	private Exception workflowException;
	public Exception WorkflowException
	{
		get => workflowException;
		set => workflowException = value;
	}

	private Guid workflowInstanceId;
	public Guid WorkflowInstanceId
	{
		get => workflowInstanceId;
		set => workflowInstanceId = value;
	}

	private readonly Guid workflowUniqueId;
	public Guid WorkflowUniqueId
	{
		get => workflowUniqueId;
		private init => workflowUniqueId = value;
	}

	private string name = "";
	public string Name
	{
		get => name;
		set => name = value;
	}

	private string transactionId = null;
	public string TransactionId => transactionId;

	public void SetTransactionId(
		string transactionId, 
		WorkflowTransactionBehavior transactionBehavior)
	{
		if (transactionBehavior 
		    == WorkflowTransactionBehavior.InheritExisting)
		{
			this.transactionId = transactionId;
		}
	}

	private WorkflowTransactionBehavior transactionBehavior 
		= WorkflowTransactionBehavior.InheritExisting;
	public WorkflowTransactionBehavior TransactionBehavior
	{
		get => transactionBehavior;
		set => transactionBehavior = value;
	}

	private IWorkflowBlock workflowBlock;
	public IWorkflowBlock WorkflowBlock
	{
		get => workflowBlock;
		set => workflowBlock = value;
	}

	private WorkflowHost host;
	public WorkflowHost Host
	{
		get => host;
		set => host = value;
	}

	private WorkflowEngine callingWorkflow;
	public WorkflowEngine CallingWorkflow
	{
		get => callingWorkflow;
		set
		{
			callingWorkflow = value;
			if (callingWorkflow == null)
			{
				return;
			}
			// Inherit caller's workflow instance description
			if (RuntimeDescription == "")
			{
				RuntimeDescription = callingWorkflow.RuntimeDescription;
			}
			if (Notification == "")
			{
				Notification = callingWorkflow.Notification;
			}

			if (ResultMessage == "")
			{
				ResultMessage = callingWorkflow.ResultMessage;
			}
		}
	}

	private RuleEngine ruleEngine;
	public RuleEngine RuleEngine => ruleEngine;

	private readonly Hashtable inputContexts = new();
	/// <summary>
	/// Input context stores when this block is called as a subworkflow
	/// </summary>
	public Hashtable InputContexts => inputContexts;

	public object ReturnValue
	{
		get
		{
			foreach (IContextStore resultContext 
			         in WorkflowBlock.ChildItemsByType<ContextStore>(
				         ContextStore.CategoryConst))
			{
				if (resultContext.IsReturnValue)
				{
					return RuleEngine.GetContext(resultContext);
				}
			}
			return null;
		}
	}

	private readonly Hashtable parentContexts = new();
	/// <summary>
	/// Context stores of the parent block that this block will use
	/// </summary>
	public Hashtable ParentContexts => parentContexts;

	private IPersistenceProvider persistenceProvider;
	public IPersistenceProvider PersistenceProvider
	{
		get => persistenceProvider;
		set => persistenceProvider = value;
	}

	private int iterationNumber = 0;
	public int IterationNumber
	{
		get => iterationNumber;
		set => iterationNumber = value;
	}

	private int iterationTotal = 0;
	public int IterationTotal
	{
		get => iterationTotal;
		set => iterationTotal = value;
	}

	private string runtimeDescription = "";
	public string RuntimeDescription
	{
		get => runtimeDescription;
		set => runtimeDescription = value;
	}

	private string notification = "";
	public string Notification
	{
		get => notification;
		set => notification = value;
	}

	private string resultMessage = "";
	public string ResultMessage
	{
		get => resultMessage;
		set => resultMessage = value;
	}

	private bool isRepeatable = false;
	public bool IsRepeatable
	{
		get => isRepeatable;
		set => isRepeatable = value;
	}

	#endregion

	#region Public Static Methods
	public static WorkflowEngine PrepareWorkflow(
		IWorkflow workflow, Hashtable parameters, bool isRepeatable, 
		string titleName)
	{
		var persistenceService 
			= ServiceManager.Services.GetService<IPersistenceService>();
		var workflowEngine = new WorkflowEngine();
		workflowEngine.PersistenceProvider 
			= persistenceService.SchemaProvider;
		workflowEngine.IsRepeatable = isRepeatable;
		foreach (DictionaryEntry entry in parameters)
		{
			string parameterName = (string)entry.Key;
			ISchemaItem context = workflow.GetChildByName(
				parameterName, ContextStore.CategoryConst);
			if (context == null)
			{
				throw new ArgumentOutOfRangeException(
					"name", parameterName, 
					string.Format(
						ResourceUtils.GetString(
							"ErrorWorkflowParameterNotFound"), 
						((ISchemaItem)workflow).Path));
			}
			workflowEngine.InputContexts.Add(
				context.PrimaryKey, entry.Value);
		}
		workflowEngine.TransactionBehavior = workflow.TransactionBehavior;
		workflowEngine.WorkflowBlock = workflow;
		workflowEngine.Name = titleName;
		return workflowEngine;
	}
	#endregion

	#region Public Methods
	public void RunWorkflowFromHost()
	{
		if (ProfilingTools.IsDebugEnabled)
		{
			localOperationTimer.Start(GetHashCode());
		}
		RunWorkflow();
	}

	private void LogWorkflowEnd(Stopwatch stopwatch)
	{
		ProfilingTools.LogDuration(
			logEntryType: "WF",
			path: Name, 
			id: workflowInstanceId.ToString(), 
			stoppedStopwatch: stopwatch);
		if (CallingWorkflow == null)
		{
			ProfilingTools.LogWorkFlowEnd();
		}
	}

	private void LogBlockIteration(Stopwatch stopwatch)
	{
		ProfilingTools.LogDuration(
			logEntryType: "Iteration",
			path: $"{((ISchemaItem) WorkflowBlock).Path}/{iterationNumber}",
			id: workflowInstanceId.ToString(),
			stoppedStopwatch: stopwatch);
	}

	private void RunWorkflow()
	{
		if (WorkflowBlock == null)
		{
			throw new InvalidOperationException(
				ResourceUtils.GetString("ErrorNoWorkflow"));
		}
		if (Host == null)
		{
			throw new InvalidOperationException(
				ResourceUtils.GetString("ErrorNoHost"));
		}
		taskResults.Clear();
		try
		{
			if (log.IsDebugEnabled)
			{
				log.Debug("---------------------------------------------------------------------------------------");
				log.Debug("------------------- Starting workflow: " + WorkflowBlock.Name);
				log.Debug("------------------- Transaction ID: " + TransactionId);
				log.Debug("---------------------------------------------------------------------------------------");
			}
			if (CallingWorkflow == null)
			{
				RuntimeDescription = "";
				Notification = "";
				ResultMessage = "";
			}
			if (IsTrace(WorkflowBlock))
			{
				tracingService.TraceWorkflow(
					workflowInstanceId: WorkflowInstanceId,
					workflowId:(Guid) WorkflowBlock.PrimaryKey["Id"],
					WorkflowBlock.Name);
			}
			// Initialize all context stores (resume paused ones?)
			var contextStores = new Hashtable();
			// Initialize RuleEngine for this session
			Guid tracingWorkflowId = IsTrace(WorkflowBlock) 
				? WorkflowInstanceId 
				: Guid.Empty;
			ruleEngine = RuleEngine.Create(
				contextStores, TransactionId, tracingWorkflowId);
			foreach (IContextStore contextStore 
			         in WorkflowBlock.ChildItemsByType<ContextStore>(
				         ContextStore.CategoryConst))
			{
				if (log.IsDebugEnabled)
				{
					log.Debug(
						"Initializing data store: " + contextStore?.Name);
				}
				// Otherwise we generate an empty store
				if (contextStore!.DataType == OrigamDataType.Xml)
				{
					switch (contextStore.Structure)
					{
						case DataStructure dataStructure:
						{
							DataSet dataset =
								datasetGenerator.CreateDataSet(dataStructure,
									contextStore.DefaultSet);
							if (contextStore.DisableConstraints)
							{
								dataset.EnforceConstraints = false;
							}
							contextStores.Add(
								contextStore.PrimaryKey, 
								DataDocumentFactory.New(dataset));
							break;
						}
						case XsdDataStructure:
						{
							contextStores.Add(
								contextStore.PrimaryKey, 
								new XmlContainer());
							break;
						}
						case null:
						{
							throw new NullReferenceException(
								ResourceUtils.GetString("ErrorNoXmlStore"));
						}
						default:
						{
							throw new ArgumentOutOfRangeException(
								"DataType",
								contextStore.DataType,
								ResourceUtils.GetString(
									"ErrorUnsupportedXmlStore"));
						}
					}
				} 
				else
				{
					contextStores.Add(contextStore.PrimaryKey, null);
				}
				if (InputContexts.ContainsKey(contextStore.PrimaryKey))
				{
					// If we have input data, we use them
					if (log.IsDebugEnabled)
					{
						log.Debug("Passing input context");
					}
					if (IsTrace(WorkflowBlock))
					{
						tracingService.TraceStep(
							workflowInstanceId: WorkflowInstanceId,
							stepPath: WorkflowBlock
							.Path,
							stepId: Guid.Empty,
							category: "Input Context",
							subCategory: contextStore.Name,
							remark: "",
							data1: ContextData(
								InputContexts[contextStore.PrimaryKey]), 
							data2: null,
							message: null);
					}
					MergeContext(
						resultContextKey: contextStore.PrimaryKey,
						inputContext: InputContexts[contextStore.PrimaryKey], 
						step: null, 
						contextName: contextStore.Name,
						method: ServiceOutputMethod.AppendMergeExisting);
				}
			}
			// Include all contexts from the parent block
			foreach (DictionaryEntry entry in ParentContexts)
			{
				contextStores.Add(entry.Key, entry.Value);
			}
			List<AbstractWorkflowStep> tasks = WorkflowBlock.ChildItemsByType<AbstractWorkflowStep>(
				AbstractWorkflowStep.CategoryConst);
			// Set states of each task to "not run"
			foreach (AbstractWorkflowStep task in tasks)
			{
				SetStepStatus(task, WorkflowStepResult.Ready);
			}
			// clear input contexts - they will not be needed anymore
			InputContexts.Clear();
			ResumeWorkflow();
		} 
		catch (Exception ex)
		{
			HandleWorkflowException(ex);
		}
	}

	public bool IsTrace(IWorkflowStep workflowStep)
	{
		return workflowStep switch
		{
			// step can be null e.g. when called
			// from workflow screen in Architect
			null => false,
			Schema.WorkflowModel.Workflow 
				when workflowStep.TraceLevel 
				     == Origam.Trace.InheritFromParent 
				=> Trace,
			_ => workflowStep.Trace switch
			{
				// when all workflow has InheritFromParent then gets Trace
				// from Parent Workflow
				Origam.Trace.InheritFromParent => Trace,
				Origam.Trace.Yes => true,
				Origam.Trace.No => false,
				_ => false
			}
		};
	}

	private void ResumeWorkflow()
	{
		List<AbstractWorkflowStep> tasks = WorkflowBlock.ChildItemsByType<AbstractWorkflowStep>(
			AbstractWorkflowStep.CategoryConst);
		if (tasks.Count == 0)
		{
			FinishWorkflow(null);
			return;
		}
		for (int i = 0; i < tasks.Count; i++)
		{
			if (WorkflowCompleted())
			{
				FinishWorkflow(null);
				break;
			}
			// Check if the task is ready to run by start event
			if (CanStepRun(tasks[i] as IWorkflowStep))
			{
				// Now check if the task will ever run by startup rule
				if (EvaluateStartRuleTimed(tasks[i] as IWorkflowStep))
				{
					var currentModelStep = tasks[i] as IWorkflowStep;
					IWorkflowEngineTask engineTask 
						= WorkflowTaskFactory.GetTask(currentModelStep);
					engineTask.Engine = this;
					engineTask.Step = currentModelStep;
					if (log.IsDebugEnabled)
					{
						log.Debug("---------------------------------------------------------------------------------------");
						log.Debug("Starting " + engineTask.GetType().Name + ": " + currentModelStep?.Name);
					}
					workflowStackTrace.RecordStepStart(
						WorkflowBlock.Name, currentModelStep?.Name);
					SetStepStatus(
						currentModelStep, WorkflowStepResult.Running);
					engineTask.Finished += OnEngineTaskFinished;
					engineTask.Execute();
					break;
				}
				// Task will never run, startup rule returned false
				SetStepStatus(
					tasks[i] as IWorkflowStep, WorkflowStepResult.NotRun);
			}	
			if (i == tasks.Count - 1)
			{
				// let's start over
				i = -1;
			}
		}
	}

	private void HandleStepException(
		IWorkflowStep step, Exception exception)
	{
		SetStepStatus(step, WorkflowStepResult.Failure);
		if (log.IsErrorEnabled)
		{
			log.Error(
				$"{step?.GetType().Name} {step?.Path} failed.");
		}
		// Trace the error
		if (IsTrace(step))
		{
			tracingService.TraceStep(
				workflowInstanceId: WorkflowInstanceId, 
				stepPath: step!.Path, 
				stepId: step!.Id, 
				category: "Process", 
				subCategory: "Error", 
				remark: null, 
				data1: null, 
				data2: null, 
				exception.Message);
		}
		// suppress all tasks that had not run yet and have no dependencies
		List<AbstractWorkflowStep> tasks = WorkflowBlock.ChildItemsByType<AbstractWorkflowStep>(
			AbstractWorkflowStep.CategoryConst);
		for (int i = 0; i < tasks.Count; i++)
		{
			var siblingStep = tasks[i] as IWorkflowStep;
			if ((siblingStep!.Dependencies.Count == 0) 
			    && taskResults[siblingStep.PrimaryKey] 
			    == WorkflowStepResult.Ready)
			{
				SetStepStatus(siblingStep, WorkflowStepResult.NotRun);
			}
		}
		if (IsFailureHandled(step))
		{
			caughtException = exception;
			return;
		}
		// cancel the workflow and rethrow the exception up, if root workflow
		HandleWorkflowException(GetStepException(exception, step!.Name));
	}

	/// <summary>
	/// Returns true if there is a task in the workflow that handles failures.
	/// </summary>
	/// <returns></returns>
	private bool IsFailureHandled(IWorkflowStep failedStep)
	{
		List<AbstractWorkflowStep> tasks = WorkflowBlock.ChildItemsByType<AbstractWorkflowStep>(
			AbstractWorkflowStep.CategoryConst);
		foreach (IWorkflowStep step in tasks)
		{
			var dependencyOnFailedStep = step.Dependencies
				.FirstOrDefault(
					dependency => dependency.Task == failedStep);
			if (dependencyOnFailedStep 
			    is { StartEvent: WorkflowStepStartEvent.Failure })
			{
				return true;
			}
		}
		return false;
	}

	private void HandleWorkflowException(Exception exception)
	{
		WorkflowException = exception;
		var keys = taskResults.Keys.ToList();
		foreach (Key key in keys)
		{
			if (taskResults[key] 
			    == WorkflowStepResult.Ready)
			{
				taskResults[key] = WorkflowStepResult.NotRun;
			}
		}
		if (IsTrace(WorkflowBlock))
		{
			string recursiveExceptionText = exception.Message;
			Exception recursiveException = exception;
			while (recursiveException.InnerException != null)
			{
				recursiveExceptionText 
					+= Environment.NewLine 
					   + "-------------------------------- " 
					   + Environment.NewLine 
					   + recursiveException.InnerException.Message;
				recursiveException = recursiveException.InnerException;
			}
			tracingService.TraceStep(
				workflowInstanceId: WorkflowInstanceId, 
				stepPath: WorkflowBlock?.Path, 
				stepId: (Guid)WorkflowBlock.PrimaryKey["Id"], 
				category: "Process", 
				subCategory: "Error", 
				remark: null, 
				data1: recursiveExceptionText, 
				data2: recursiveException.StackTrace, 
				exception.Message);
		}
		if (exception is not WorkflowCancelledByUserException 
		    && log.IsErrorEnabled)
		{
			log.LogOrigamError(
				$"{exception.Message}\n{workflowStackTrace}", exception);
		}
		FinishWorkflow(exception);
	}

	private void SetStepStatus(
		IWorkflowStep step, WorkflowStepResult status)
	{
		taskResults[step.PrimaryKey] = status;
	}

	private WorkflowStepResult StepStatus(IWorkflowStep step)
	{
		return taskResults[step.PrimaryKey];
	}

	private void EvaluateEndRuleTimed(
		IEndRule rule, object data, IWorkflowStep step)
	{
		ProfilingTools.ExecuteAndLogDuration(
			action: () => EvaluateEndRule(rule, data, step),
			logEntryType: "Validation Rule",
			task: step);
	}

	public void EvaluateEndRule(
		IEndRule rule, object data, IWorkflowStep step)
	{
		if (rule == null)
		{
			return;
		}
		RuleExceptionDataCollection result = RuleEngine.EvaluateEndRule(
			rule: rule, 
			data: data, 
			parentIsTracing: IsTrace(step)
		);
		if (step != null && IsTrace(step))
		{
			tracingService.TraceStep(
				workflowInstanceId: WorkflowInstanceId,
				stepPath: step!.Path,
				stepId: (Guid)step.PrimaryKey["Id"],
				category: "End Rule",
				subCategory: "Input",
				remark: step.ValidationRuleContextStore.Name,
				data1: ContextData(data),
				data2: null,
				message: null);
		}
		// if there are some exceptions, we actually throw them
		if ((result != null) && (result.Count != 0))
		{
			throw new RuleException(result);
		}		
	}

	private Exception GetStepException(Exception exception, string stepName)
	{
		return 
			exception is WorkflowCancelledByUserException or RuleException 
				? exception 
				: new OrigamException(
					exception.Message, stepName, exception);
	}
	#endregion

	#region Private Methods
	private bool WorkflowCompleted()
	{
		foreach (var entry in taskResults)
		{
			WorkflowStepResult result = entry.Value;
			if (result 
			    is WorkflowStepResult.Ready or WorkflowStepResult.Running)
			{
				return false;
			}
		}
		return true;
	}

	private bool CanStepRun(IWorkflowStep step)
	{
		// Check if this task has been already completed, don't run it again
		if (StepStatus(step) != WorkflowStepResult.Ready)
		{
			return false;
		}
		foreach (WorkflowTaskDependency dependency in step.Dependencies)
		{
			try
			{
				if (!taskResults.ContainsKey(dependency.Task.PrimaryKey))
				{
					throw new Exception(
						"Workflow task dependency invalid. Task: " 
						+ step.Name);
				}
			}
			catch (Exception ex)
			{
				throw new Exception(
					"Workflow task dependency invalid. Task: " 
					+ step.Name, ex);
			}
			WorkflowStepResult dependencyResult = StepStatus(dependency.Task);
			switch (dependencyResult)
			{
				case WorkflowStepResult.Running:
				{
					return false;
				}
				case WorkflowStepResult.FailureNotRun:
				{
					SetStepStatus(step, WorkflowStepResult.FailureNotRun);
					return false;
				}
				case WorkflowStepResult.NotRun:
				{
					// If dependent task did not run
					// and we don't care about result, it's ok
					if (dependency.StartEvent != WorkflowStepStartEvent.Finish)
					{
						// We check if any of tasks we depend on has state NotRun.
						// In that case current task will not run as well.
						SetStepStatus(step, WorkflowStepResult.NotRun);
						return false;
					}
					break;
				}
				default:
				{
					switch (dependencyResult)
					{
						case WorkflowStepResult.Success
							when dependency.StartEvent
							     == WorkflowStepStartEvent.Failure:
						{
							// for failures we only start tasks
							// marked to start on failure
							SetStepStatus(
								step, WorkflowStepResult.FailureNotRun);
							return false;
						}
						case WorkflowStepResult.Failure
							when dependency.StartEvent
							     != WorkflowStepStartEvent.Failure:
						{
							SetStepStatus(
								step, WorkflowStepResult.FailureNotRun);
							return false;
						}
						case WorkflowStepResult.Ready:
						{
							// The dependent task did not run, yet.
							// So we still have to wait a bit.
							return false;
						}
					}
					break;
				}
			}
		}
		return true;
	}

	private bool EvaluateStartRuleTimed(IWorkflowStep task)
	{
		string path = task is ISchemaItem schemaItem 
			? schemaItem.Path 
			: "";
		return ProfilingTools.ExecuteAndLogDuration(
			funcToExecute: () => EvaluateStartRule(task),
			logEntryType: "Start Rule",
			path: path + "/Start Rule",
			id: task.NodeId,
			logOnlyIf: () => task.StartConditionRule != null);
	}

	private bool EvaluateStartRule(IWorkflowStep task)
	{
		// check features
		if (!parameterService.IsFeatureOn(task.Features))
		{
			if (log.IsDebugEnabled)
			{
				log.Debug(
					"Step will not execute because of feature being turned off.");
			}
			return false;
		}
		if (!SecurityManager.GetAuthorizationProvider()
			    .Authorize(SecurityManager.CurrentPrincipal, task.Roles))
		{
			if (log.IsDebugEnabled)
			{
				log.Debug(
					"Step will not execute because the user has not been authorized.");
			}
			return false;
		}
		// If there is no start rule, we always start the task
		if (task.StartConditionRule == null)
		{
			return true;
		}
		if (log.IsDebugEnabled)
		{
			log.Debug("Evaluating startup rule for step " + task.Name);
		}
		var result = (bool) RuleEngine.EvaluateRule(
			rule: task.StartConditionRule, 
			data: task.StartConditionRuleContextStore, 
			contextPosition: null,
			parentIsTracing: IsTrace(task));
		if (log.IsDebugEnabled)
		{
			log.Debug("Rule evaluated and returned " + result);
		}
		return result;
	}

	internal void EvaluateEndRule(IWorkflowStep step)
	{
		// If there is no validation rule, we return
		if (step.ValidationRule == null)
		{
			return;
		}
		if (log.IsDebugEnabled)
		{
			log.Debug("Evaluating validation rule for step " + step.Name);
		}
		RuleExceptionDataCollection result = RuleEngine.EvaluateEndRule(
			step.ValidationRule, step.ValidationRuleContextStore);
		if (result == null)
		{
			throw new OrigamException(
				"Programming error: there is not any " 
				+ $"RuleExceptionDataCollection in the output of the validation rule `{step.ValidationRule.Name}' ({step.ValidationRule.PrimaryKey}). " 
				+ "Please review the rule and add <RuleExceptionDataCollection> tag.");
		}
		// if there are some exceptions, we actually throw them
		if (result.Count != 0)
		{
			throw new RuleException(result);
		}
	}

	internal WorkflowEngine GetSubEngine(
		IWorkflowBlock block, WorkflowTransactionBehavior transactionBehavior)
	{
		var subEngine = new WorkflowEngine();
		// Set same properties as we have
		subEngine.PersistenceProvider = PersistenceProvider;
		subEngine.CallingWorkflow = this;
		subEngine.WorkflowBlock = block;
		subEngine.Host = Host;
		subEngine.TransactionBehavior = transactionBehavior;
		subEngine.WorkflowInstanceId = WorkflowInstanceId;
		subEngine.SetTransactionId(TransactionId, transactionBehavior);
		subEngine.IterationTotal = IterationTotal;
		subEngine.IterationNumber = IterationNumber;
		subEngine.Trace = Trace;
		return subEngine;
	}

	internal void ExecuteSubEngineWorkflow(WorkflowEngine subEngine)
	{
		Host.ExecuteWorkflow(subEngine);
	}

	internal object CloneContext(object context, bool returnDataSet)
	{
		return context switch
		{
			IDataDocument document when returnDataSet =>
				document.DataSet.Copy(),
			IDataDocument document => DataDocumentFactory.New(
				document.DataSet.Copy()),
			IXmlContainer container => container.Clone(),
			// value types - we return the value itself, don't need a clone
			_ => context
		};
	}

	public IWorkflowStep Step(Key key)
	{
		return PersistenceProvider.RetrieveInstance<IWorkflowStep>(
			(Guid)key["Id"]);
	}

	private ContextStore GetContextStore(Key key)
	{
		return PersistenceProvider.RetrieveInstance<ContextStore>(
			(Guid)key["Id"]);
	}

	internal string ContextStoreName(Key key)
	{
		return GetContextStore(key).Name;
	}

	internal DataStructureRuleSet ContextStoreRuleSet(Key key)
	{
		return GetContextStore(key).RuleSet;
	}

	internal OrigamDataType ContextStoreType(Key key)
	{
		return GetContextStore(key).DataType;
	}

	internal bool MergeContext(
		Key resultContextKey, 
		object inputContext, 
        IWorkflowStep step, 
		string contextName, 
        ServiceOutputMethod method)
	{
		if (method == ServiceOutputMethod.Ignore)
		{
			return false;
		}
		object resultContext = RuleEngine.GetContext(resultContextKey);
		bool changed = false;
		if (log.IsInfoEnabled)
		{
			string stepNameLog = "";
			if (step != null) 
			{
                stepNameLog 
	                = ", Step '" + step!.Path + "'";
			}
			log.Info("Merging context '" + contextName + "'" + stepNameLog);
		}
		try
		{
			if ((step != null) && IsTrace(step))
			{
				tracingService.TraceStep(
					workflowInstanceId: WorkflowInstanceId,
					stepPath: step.Path,
					stepId: (Guid)step.PrimaryKey["Id"],
					category: "Merge Context",
					subCategory: "Input",
					remark: contextName,
					data1: ContextData(inputContext),
					data2: null,
					message: null);
			}
			var inputDataDoc = inputContext as IDataDocument;
			var inputXmlDoc = inputContext as IXmlContainer;
			var resultDataDoc = resultContext as IDataDocument;
			var resultXmlDoc = resultContext as IXmlContainer;
			if (inputContext == null || inputContext == DBNull.Value)
			{
				return false;
			}
			if (inputDataDoc != null && resultDataDoc != null)
			{
				DataSet input = inputDataDoc.DataSet;
				DataSet output = resultDataDoc.DataSet;
				switch (method)
				{
					case ServiceOutputMethod.AppendMergeExisting
						or ServiceOutputMethod.FullMerge:
					{
						changed = RuleEngine.Merge(
							inout_dsTarget: output, 
							in_dsSource: input, 
							in_bTrueDelete: method == ServiceOutputMethod.FullMerge, 
							in_bPreserveChanges: false, 
							in_bSourceIsFragment: false, 
							preserveNewRowState: true);
						break;
					}
					case ServiceOutputMethod.DeleteMatches:
					{
						foreach (DataTable inputTable in input.Tables)
						{
							if (!output.Tables.Contains(inputTable.TableName))
							{
								continue;
							}
							DataTable outputTable 
								= output.Tables[inputTable.TableName];
							foreach(DataRow inputRow in inputTable.Rows)
							{
								object[] inputRowPrimaryKey 
									= DatasetTools.PrimaryKey(inputRow);
								DataRow rowToDelete = outputTable.Rows.Find(
									inputRowPrimaryKey);
								if (rowToDelete != null)
								{
									rowToDelete.Delete();
									changed = true;
								}
							}
						}
						break;
					}
					default:
					{
						throw new ArgumentOutOfRangeException(
							"method", method, "Unsupported merge method.");
					}
				}
			}
			else if (inputXmlDoc != null)
			{
				ContextStore contextStore = GetContextStore(resultContextKey);
				if (contextStore.DataType == OrigamDataType.String)
				{
					RuleEngine.SetContext(
						resultContextKey, inputXmlDoc.Xml.InnerText);
				}
				else
				{
					if (resultXmlDoc == null)
					{
						throw new Exception(
							"Cannot merge data into a context, which is not XML type. Context: " 
							+ contextName + ", type: " 
							+ (resultContext == null 
								? "NULL" 
								: resultContext.GetType().ToString()));
					}
					changed = true;
					if (inputXmlDoc.Xml.DocumentElement != null)
					{
						// copy document element, if it does not exist already
						if (resultXmlDoc.Xml.DocumentElement == null)
						{
							if (resultDataDoc != null)
							{
								bool previousEnforceConstraints 
									= resultDataDoc.DataSet.EnforceConstraints;
								resultDataDoc.DataSet.EnforceConstraints = false;
								resultDataDoc.AppendChild(
									inputXmlDoc.Xml.DocumentElement, true);
								try {
									resultDataDoc.DataSet.EnforceConstraints 
										= previousEnforceConstraints;
								} 
								catch (Exception ex)
								{
									throw new Exception(
										DebugClass.ListRowErrors(
											resultDataDoc.DataSet), ex);
								}
							}
							else
							{
								XmlNode newDoc = resultXmlDoc.Xml.ImportNode(
									inputXmlDoc.Xml.DocumentElement, true);
								resultXmlDoc.Xml.AppendChild(newDoc);
							}
						}
						else
						{
							// otherwise copy each sub node
							foreach (XmlNode node in inputXmlDoc.Xml
								         .DocumentElement.ChildNodes)
							{
								if (node is not XmlDeclaration)
								{
									resultXmlDoc.DocumentElementAppendChild(
										node);
								}
							}
						}
					}
				}
			}
			else
			{
				// Web Service support - they send XML as string
				changed = true;
				var resultXml = RuleEngine.GetContext(resultContextKey) 
					as IXmlContainer;
				var xmlDataDoc = resultXml as IDataDocument;
				bool previousEnforceConstraints = false;
				if (xmlDataDoc != null)
				{
					previousEnforceConstraints 
						= xmlDataDoc.DataSet.EnforceConstraints;
					xmlDataDoc.DataSet.EnforceConstraints = false;
				}
				var inputString = inputContext as string;
				if ((resultXml != null) && (inputString != null))
				{
					resultXml.LoadXml(inputString);
					if (xmlDataDoc != null)
					{
						// set default values (loading xml will not set them automatically)
						SetDataSetDefaultValues(xmlDataDoc.DataSet);
						try
						{
							xmlDataDoc.DataSet.EnforceConstraints 
								= previousEnforceConstraints;
						}
						catch (Exception ex)
						{
							throw new Exception(DebugClass.ListRowErrors(
								xmlDataDoc.DataSet), ex);
						}
						object profileId 
							= SecurityManager.CurrentUserProfile().Id;
						foreach (DataTable table in xmlDataDoc.DataSet.Tables)
						{
							foreach (DataRow row in table.Rows)
							{
								DatasetTools.UpdateOrigamSystemColumns(
									row, true, profileId);
							}
						}
					}
				}
				// everything else (simple data types) - we just copy the value
				else
				{
					OrigamDataType contextType 
						= ContextStoreType(resultContextKey);
					RuleEngine.ConvertStringValueToContextValue(
						contextType, inputString, ref inputContext);
					RuleEngine.SetContext(resultContextKey, inputContext);
				}
			}
			if ((step != null) && IsTrace(step))
			{
				tracingService.TraceStep(
					workflowInstanceId: WorkflowInstanceId,
					stepPath: step.Path,
					stepId: (Guid)step.PrimaryKey["Id"],
					category: "Merge Context",
					subCategory: "Result",
					remark: contextName,
					data1: changed 
						? ContextData(RuleEngine.GetContext(resultContextKey)) 
						: "-- no change --",
					data2: null,
					message: null);
			}
			DataStructureRuleSet ruleSet = ContextStoreRuleSet(resultContextKey);
			if (changed 
			   && (ruleSet != null) 
			   && step is (null or IWorkflowTask or CheckRuleStep) 
				   and not UIFormTask)
			{
					
				ProcessRulesTimed(resultContextKey, ruleSet, step);
				if ((step != null) && IsTrace(step))
				{
					tracingService.TraceStep(
						workflowInstanceId: WorkflowInstanceId,
						stepPath: step.Path,
						stepId: (Guid)step.PrimaryKey["Id"],
						category: "Rule Processing",
						subCategory: "Result",
						remark: contextName,
						data1: ContextData(RuleEngine.GetContext(
							resultContextKey)),
						data2: null,
						message: null);
				}
				if ((step == null) && IsTrace(WorkflowBlock))
				{
					tracingService.TraceStep(
						workflowInstanceId: WorkflowInstanceId,
						stepPath: WorkflowBlock.Path,
						stepId: (Guid)WorkflowBlock.PrimaryKey["Id"],
						category: "Rule Processing",
						subCategory: "Result",
						remark: contextName,
						data1: ContextData(
							RuleEngine.GetContext(resultContextKey)),
						data2: null,
						message: null);
				}
			}
		}
		catch (Exception ex)
		{
			string stepNameLog = "";
			if (step != null)
			{
				stepNameLog 
					= ", Step '" + step.Path + "'";
			}
			string inputString = inputContext as string ?? "";
			throw new Exception(
				"Merge context '" + contextName + "'" + stepNameLog 
				+ " failed. InputContextValue: " + inputString 
				+ ". Original exception message: " + ex.Message, ex);
		}
		if (log.IsInfoEnabled)
		{
			string stepNameLog = "";
			if (step != null)
			{
				stepNameLog 
					= ", Step '" + step.Path + "'";
			}
			log.Info(
				"Finished merging context '" + contextName + "'" + stepNameLog);
		}
		return changed;
	}

	private void SetDataSetDefaultValues(DataSet dataSet)
	{
		foreach (DataTable table in dataSet.Tables)
		{
			foreach (DataRow row in table.Rows)
			{
				foreach (DataColumn column in table.Columns)
				{
					if (!column.AllowDBNull 
					    && (column.DefaultValue != null) 
					    && (row[column] == DBNull.Value))
					{
						row[column] = column.DefaultValue;
					}
				}
			}
		}
		
	}

	private void ProcessRulesTimed(Key resultContextKey,
		DataStructureRuleSet ruleSet,IWorkflowStep step)
	{				
		ProfilingTools.ExecuteAndLogDuration(
			action: () =>
			{
				ruleEngine.ProcessRules(
					data: RuleEngine.GetContext(resultContextKey) 
						as IDataDocument,
					ruleSet: ruleSet,
					contextRow: null);
			},
			logEntryType: "Context RuleSet",
			task:step);
	}

	public static string ContextData(object context)
	{
		switch (context)
		{
			case IXmlContainer xmlContainer:
			{
				var stringBuilder = new StringBuilder();
				var stringWriter = new StringWriter(stringBuilder);
				var xmlTextWriter = new XmlTextWriter(stringWriter);
				xmlTextWriter.Formatting = Formatting.Indented;
				xmlContainer.Xml.WriteTo(xmlTextWriter);
				xmlTextWriter.Close();
				stringWriter.Close();
				return stringBuilder.ToString();
			}
			case DataSet dataSet:
			{
				return dataSet.GetXml();
			}
			case null:
			{
				return "";
			}
			default:
			{
				return context.ToString();
			}
		}
	}

	internal string GetTaskDescription(IWorkflowStep task)
	{
		string append = RuntimeDescription;
		if (append != "")
		{
			append = " " + append;
		}
		var documentationService 
			= ServiceManager.Services.GetService<IDocumentationService>();
		if (documentationService == null)
		{
			return task.Name + append;
		}
		string documentation = documentationService.GetDocumentation(
			(Guid)task.PrimaryKey["Id"],
			DocumentationType.USER_WFSTEP_DESCRIPTION);
		if (string.IsNullOrEmpty(documentation))
		{
			documentation = task.Name;
		}
		documentation += append;
		if (IterationTotal > 0)
		{
			documentation += $" ({IterationNumber}/{IterationTotal})";
		}
		return documentation;
	}

	#endregion

	private void OnEngineTaskFinished(
		object sender, WorkflowEngineTaskEventArgs e)
	{
		var engineTask = sender as IWorkflowEngineTask;
		IWorkflowStep currentModelStep = engineTask?.Step;
		try
		{
			if (e.Exception == null)
			{
				IWorkflowTask task = currentModelStep as IWorkflowTask;
				// Check if results are ok with the EndRule
				if (currentModelStep?.ValidationRule != null)
				{
					if (currentModelStep.ValidationRuleContextStore == null)
					{
						throw new NullReferenceException(
							$"End Rule Context Store is not set. Task '{task?.Name}'");
					}
					object validationRuleStore = RuleEngine.GetContext(
                        currentModelStep.ValidationRuleContextStore);
					if ((task != null) 
					    && task.OutputContextStore.PrimaryKey.Equals(
						    task.ValidationRuleContextStore.PrimaryKey))
					{
						validationRuleStore = engineTask.Result;
					}
					try
					{
						EvaluateEndRuleTimed(
							currentModelStep.ValidationRule, 
							validationRuleStore, 
                            currentModelStep);
					}
					catch (RuleException ruleException)
					{
						if (ruleException.IsSeverityHigh)
						{
							throw;
						}
					}
				}
				if (task != null)
				{
					if((task.OutputContextStore == null) 
					   && (task.OutputMethod != ServiceOutputMethod.Ignore))
					{
						throw new NullReferenceException(
                            ResourceUtils.GetString(
	                            "ErrorNoOutputContext", task.Name));
					}
					// nothing has happened after evaluating end rule, we process the results
					bool doMerge = true;
#if ORIGAM_SERVER
					if ((task is UIFormTask) 
					    && (task.OutputMethod == ServiceOutputMethod.FullMerge))
					{
						doMerge = false;
					}
#endif
					if (doMerge)
					{
						ProfilingTools.ExecuteAndLogDuration(
							action: () =>
							{
								MergeContext(
									task.OutputContextStore.PrimaryKey,
									engineTask.Result,
									task, 
									task.OutputContextStore.Name,
									task.OutputMethod);
							},
							logEntryType: "Merge",
							task: task);
					}
				}
				SetStepStatus(currentModelStep, WorkflowStepResult.Success);
				if (log.IsDebugEnabled)
				{
					log.Debug(
						$"{engineTask?.GetType().Name} {currentModelStep?.Name} finished successfully.");
				}
				if (Host.SupportsUI)
				{
					ProfilingTools.ExecuteAndLogDuration(
						action: () =>
						{
							Host.OnWorkflowUserMessage(
								engine: this,
								message: GetTaskDescription(currentModelStep), 
								exception: null,
								popup: false);
						},
						logEntryType: "Documentation",
						task: task);
				}
			}
			else
			{
				HandleStepException(currentModelStep, e.Exception);
			}
		}
		catch (Exception ex)
		{
			HandleStepException(currentModelStep, ex);
		}
		finally
		{
			if (engineTask != null)
			{
				engineTask.Engine = null;
				engineTask.Step = null;
			}
		}
		ResumeWorkflow();
	}

	private void FinishWorkflow(Exception exception)
	{
		if (Host == null) 
		{
			return; // already disposed from the host from a preceding task
		}
		if (log.IsDebugEnabled)
		{
			log.Debug($"Block '{WorkflowBlock?.Name}' completed");
			// Show finish screen if this is the root workflow
			if (CallingWorkflow == null)
			{
				log.Debug("Workflow completed");
			}
		}
		if (ProfilingTools.IsDebugEnabled)
		{
			Stopwatch stopwatch = localOperationTimer.Stop(GetHashCode());
			if (string.IsNullOrEmpty(Name))
			{
				LogBlockIteration(stopwatch);
			} 
			else
			{
				LogWorkflowEnd(stopwatch);
			}
			OperationTimer.Global.StopAndLog(GetHashCode());
		}
		if ((exception == null) && (caughtException != null))
		{
			if (log.IsDebugEnabled)
			{
				log.Debug($"Throwing caught exception {caughtException}");
			}
			WorkflowException = caughtException;
			exception = caughtException;
		}
		Host.OnWorkflowFinished(this, exception);
	}

	#region IDisposable Members

	public void Dispose()
	{
		lock (lockObject)
		{
			disposeCallStackTraces.Add(Environment.StackTrace);
			host = null;
			callingWorkflow = null;
			datasetGenerator = null;
			workflowException = null;
			inputContexts.Clear();
			parameterService = null;
			parentContexts.Clear();
			persistenceProvider = null;
			ruleEngine = null;
			taskResults.Clear();
			tracingService = null;
			workflowBlock = null;
		}
	}
	#endregion
}