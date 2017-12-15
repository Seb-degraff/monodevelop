﻿//
// BuildOutputProcessor.cs
//
// Author:
//       Rodrigo Moya <rodrigo.moya@xamarin.com>
//
// Copyright (c) 2017 Microsoft Corp. (http://microsoft.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MonoDevelop.Ide.Editor;

namespace MonoDevelop.Ide.BuildOutputView
{
	enum BuildOutputNodeType
	{
		Build,
		Project,
		Target,
		Task,
		Error,
		Warning,
		Message,
		Diagnostics
	}

	static class BuildOutputNodeExtensions
	{
		public static IEnumerable<BuildOutputNode> SearchNodes (this BuildOutputNode buildOutputNode, BuildOutputNodeType type, string search = null)
		{
			foreach (var node in buildOutputNode.Children) {
				foreach (var item in SearchNodes (node, type, search)) {
					yield return item;
				}
			}

			if (search == null) {
				if (type == buildOutputNode.NodeType)
					yield return buildOutputNode;
			} else if (search == buildOutputNode?.Message && type == buildOutputNode.NodeType)
				yield return buildOutputNode;
		}
	}

	class BuildOutputNode
	{
		public BuildOutputNodeType NodeType { get; set; }
		public string Message { get; set; }
		public BuildOutputNode Parent { get; set; }
		public IList<BuildOutputNode> Children { get; } = new List<BuildOutputNode> ();
		public bool HasErrors { get; set; }
	}

	class BuildOutputProcessor : IDisposable
	{
		List<BuildOutputNode> rootNodes = new List<BuildOutputNode> ();
		BuildOutputNode currentNode;

		public BuildOutputProcessor (string fileName, bool removeFileOnDispose)
		{
			FileName = fileName;
			RemoveFileOnDispose = removeFileOnDispose;
		}

		public string FileName { get; }

		protected bool NeedsProcessing { get; set; } = true;

		protected bool RemoveFileOnDispose { get; set; }

		protected void Clear ()
		{
			currentNode = null;
			rootNodes = new List<BuildOutputNode> ();
			NeedsProcessing = true;
		}

		public virtual void Process ()
		{
			NeedsProcessing = false;
		}

		public void AddNode (BuildOutputNodeType nodeType, string message, bool isStart)
		{
			var node = new BuildOutputNode { NodeType = nodeType, Message = message };
			if (currentNode == null) {
				rootNodes.Add (node);
			} else {
				currentNode.Children.Add (node);
				node.Parent = currentNode;
			}

			if (isStart) {
				currentNode = node;
			}

			if (nodeType == BuildOutputNodeType.Error) {
				var p = node;
				while (p != null) {
					p.HasErrors = true;
					p = p.Parent;
				}
			}
		}

		public void EndCurrentNode (string message)
		{
			AddNode (BuildOutputNodeType.Message, message, false);
			currentNode = currentNode?.Parent;
		}

		private void ProcessChildren (TextEditor editor,
		                              IList<BuildOutputNode> children,
		                              int tabPosition,
		                              StringBuilder buildOutput,
		                              List<IFoldSegment> segments,
		                              bool includeDiagnostics,
		                              int startAtOffset)
		{
			foreach (var child in children) {
				ProcessNode (editor, child, tabPosition + 1, buildOutput, segments, includeDiagnostics, startAtOffset); 
			}
		}

		private void ProcessNode (TextEditor editor,
		                          BuildOutputNode node,
		                          int tabPosition,
		                          StringBuilder buildOutput,
		                          List<IFoldSegment> segments,
		                          bool includeDiagnostics,
		                          int startAtOffset)
		{
			if (!includeDiagnostics && node.NodeType == BuildOutputNodeType.Diagnostics) {
				return;
			}

			buildOutput.AppendLine ();

			for (int i = 0; i < tabPosition; i++) buildOutput.Append ("\t");

			int currentPosition = buildOutput.Length;
			buildOutput.Append (node.Message);

			if (node.Children.Count > 0) {
				ProcessChildren (editor, node.Children, tabPosition, buildOutput, segments, includeDiagnostics, startAtOffset);

				var segment = FoldSegmentFactory.CreateFoldSegment (editor, startAtOffset + currentPosition, buildOutput.Length - currentPosition,
																	node.Parent != null && !node.HasErrors,
																	node.Message,
																	FoldingType.Region);
				segments.Add (segment);
			}
		}

		public List<BuildOutputNode> SearchNodes (BuildOutputNodeType type, string search = null)
		{
			var elements = new List<BuildOutputNode> ();
			foreach (var item in rootNodes) {
				foreach (var node in item.SearchNodes (type, search)) {
					elements.Add (node);
				}
			}
			return elements;
		}

		public (string, IList<IFoldSegment>) ToTextEditor (TextEditor editor, bool includeDiagnostics, int startAtOffset)
		{
			var buildOutput = new StringBuilder ();
			var foldingSegments = new List<IFoldSegment> ();
			foreach (var node in rootNodes) {
				ProcessNode (editor, node, 0, buildOutput, foldingSegments, includeDiagnostics, startAtOffset);
			}
			return (buildOutput.ToString (), (IList<IFoldSegment>)foldingSegments);
		}

		bool disposed = false;

		~BuildOutputProcessor ()
		{
			Dispose (false);
		}

		public void Dispose ()
		{
			Dispose (true);
		}

		void Dispose (bool disposing)
		{
			if (!disposed) {
				if (RemoveFileOnDispose && File.Exists (FileName)) {
					File.Delete (FileName);
				}

				disposed = true;
				if (disposing) {
					GC.SuppressFinalize (this);
				}
			}
		}

		internal DocumentRegion GetDocumentRegion (BuildOutputNode errorNode)
		{


			return new DocumentRegion (10, 10, 0, 0);
		}
	}
}
