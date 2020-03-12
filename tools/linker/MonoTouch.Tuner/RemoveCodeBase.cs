﻿using System;
using Mono.Linker;
using Mono.Tuner;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xamarin.Linker;

namespace MonoTouch.Tuner {

	abstract public class RemoveCodeBase : ExceptionalSubStep {
		MethodDefinition get_nse_def;

		protected RemoveCodeBase ()
		{
		}

		protected MethodReference NotSupportedException { get; private set; }

		protected override void Process (AssemblyDefinition assembly)
		{
			// only one definition exists (in mscorlib.dll)
			if (get_nse_def == null) {
				var corlib = context.Corlib;
				var nse = corlib.MainModule.GetType ("System", "NotSupportedException");
				MethodDefinition ctor = null;
				foreach (var m in nse.Methods) {
					// no need to check HasMethods because we know there are (and nothing is removed at this stage)
					if (m.IsConstructor && m.Parameters.Count == 1 && m.Parameters [0].ParameterType.Is ("System", "String")) {
						ctor = m;
						continue;
					}
					if (m.Name != "LinkedAway")
						continue;
					get_nse_def = m;
					break;
				}

				if (get_nse_def == null) {
					// There's no System.NotSupportedException.LinkedAway() in .NET 5+, so create it.
					// https://github.com/mono/mono/blob/a5f8905afa4dfaf71095a590c298acb10d5c2662/mcs/class/corlib/System/NotSupportedException.iOS.cs#L5-L11
					get_nse_def = new MethodDefinition ("LinkedAway", MethodAttributes.Static | MethodAttributes.Assembly, nse);
					var body = get_nse_def.Body;
					var il = body.GetILProcessor ();
					il.Append (Instruction.Create (OpCodes.Ldstr, "Linked Away"));
					il.Append (Instruction.Create (OpCodes.Newobj, ctor));
					il.Append (Instruction.Create (OpCodes.Ret));
					nse.Methods.Add (get_nse_def);
				}
			}

			// import the method into the current assembly
			// i.e. a different reference exists (and must be used) in every assembly
			NotSupportedException = assembly.MainModule.ImportReference (get_nse_def);
		}

		protected void ProcessMethods (TypeDefinition type)
		{
			if (type.HasMethods) {
				MethodDefinition static_ctor = null;
				foreach (MethodDefinition method in type.Methods) {
					if (method.IsConstructor && method.IsStatic)
						static_ctor = method;
					else
						ProcessMethod (method);
				}
				if (static_ctor != null)
					type.Methods.Remove (static_ctor);
			}
		}

		new protected virtual void ProcessMethod (MethodDefinition method)
		{
			ProcessParameters (method);

			if (!method.HasBody)
				return;

			var body = new MethodBody (method);

			var il = body.GetILProcessor ();
			il.Emit (OpCodes.Call, NotSupportedException);
			il.Emit (OpCodes.Throw);

			method.Body = body;
		}

		static void ProcessParameters (MethodDefinition method)
		{
			if (!method.HasParameters)
				return;

			foreach (ParameterDefinition parameter in method.Parameters)
				parameter.Name = string.Empty;
		}
	}
}