// GtkSharp.Generation.ObjectGen.cs - The Object Generatable.
//
// Author: Mike Kestner <mkestner@speakeasy.net>
//
// (c) 2001-2002 Mike Kestner

namespace GtkSharp.Generation {

	using System;
	using System.Collections;
	using System.IO;
	using System.Text;
	using System.Xml;

	public class ObjectGen : ClassBase, IGeneratable  {

		private ArrayList strings = new ArrayList();
		private static Hashtable namespaces = new Hashtable ();

		public ObjectGen (XmlElement ns, XmlElement elem) : base (ns, elem) 
		{
			Hashtable objs;
			if (namespaces.ContainsKey (NS)) 
				objs = (Hashtable) namespaces[NS];
			else {
				objs = new Hashtable();
				namespaces.Add (NS, objs);
			}
			objs.Add (CName, QualifiedName + "," + NS.ToLower() + "-sharp");
 
			foreach (XmlNode node in elem.ChildNodes) {

				if (!(node is XmlElement)) continue;
				XmlElement member = (XmlElement) node;

				switch (node.Name) {
				case "field":
				case "callback":
					Statistics.IgnoreCount++;
					break;

				case "static-string":
					strings.Add (node);
					break;

				default:
					if (!IsNodeNameHandled (node.Name))
						Console.WriteLine ("Unexpected node " + node.Name + " in " + CName);
					break;
				}
			}
		}

		public void Generate ()
		{
			if (!DoGenerate)
				return;

			StreamWriter sw = CreateWriter ();

			sw.WriteLine ("\tusing System;");
			sw.WriteLine ("\tusing System.Collections;");
			sw.WriteLine ("\tusing System.Runtime.InteropServices;");
			sw.WriteLine ();

			sw.WriteLine("\t\t/// <summary> " + Name + " Class</summary>");
			sw.WriteLine("\t\t/// <remarks>");
			sw.WriteLine("\t\t/// </remarks>");
			sw.Write ("\tpublic class " + Name);
			string cs_parent = SymbolTable.GetCSType(Elem.GetAttribute("parent"));
			if (cs_parent != "")
				sw.Write (" : " + cs_parent);
			if (interfaces != null) {
				foreach (string iface in interfaces) {
					if (Parent != null && Parent.Implements (iface))
						continue;
					sw.Write (", " + SymbolTable.GetCSType (iface));
				}
			}
			sw.WriteLine (" {");
			sw.WriteLine ();

			GenCtors (sw);
			GenProperties (sw);
			
			bool has_sigs = (sigs != null);
			if (!has_sigs) {
				foreach (string iface in interfaces) {
					ClassBase igen = SymbolTable.GetClassGen (iface);
					if (igen.Signals != null) {
						has_sigs = true;
						break;
					}
				}
			}

			if (has_sigs && Elem.HasAttribute("parent"))
			{
				sw.WriteLine("\t\tprivate Hashtable Signals = new Hashtable();");
				GenSignals (sw, null, true);
			}

			GenMethods (sw, null, null, true);
			
			if (interfaces != null) {
				Hashtable all_methods = new Hashtable ();
				Hashtable collisions = new Hashtable ();
				foreach (string iface in interfaces) {
					ClassBase igen = SymbolTable.GetClassGen (iface);
					foreach (Method m in igen.Methods.Values) {
						if (all_methods.Contains (m.Name))
							collisions[m.Name] = true;
						else
							all_methods[m.Name] = true;
					}
				}
					
				foreach (string iface in interfaces) {
					if (Parent != null && Parent.Implements (iface))
						continue;
					ClassBase igen = SymbolTable.GetClassGen (iface);
					igen.GenMethods (sw, collisions, this, false);
					igen.GenSignals (sw, this, false);
				}
			}

			foreach (XmlElement str in strings) {
				sw.Write ("\t\tpublic static string " + str.GetAttribute ("name"));
				sw.WriteLine (" {\n\t\t\t get { return \"" + str.GetAttribute ("value") + "\"; }\n\t\t}");
			}
			
			AppendCustom(sw);

			sw.WriteLine ("\t}");

			CloseWriter (sw);
			Statistics.ObjectCount++;
		}

		private bool Validate ()
		{
			string parent = Elem.GetAttribute("parent");
			string cs_parent = SymbolTable.GetCSType(parent);
			if (cs_parent == "") {
				Console.WriteLine ("Object " + QualifiedName + " Unknown parent " + parent);
				return false;
			}

			if (ctors != null)
				foreach (Ctor ctor in ctors)
					if (!ctor.Validate()) {
						Console.WriteLine ("in Object " + QualifiedName);
						return false;
					}

			if (props != null)
				foreach (Property prop in props.Values)
					if (!prop.Validate()) {
						Console.WriteLine ("in Object " + QualifiedName);
						return false;
					}

			if (sigs != null)
				foreach (Signal sig in sigs.Values)
					if (!sig.Validate()) {
						Console.WriteLine ("in Object " + QualifiedName);
						return false;
					}

			if (methods != null)
				foreach (Method method in methods.Values)
					if (!method.Validate()) {
						Console.WriteLine ("in Object " + QualifiedName);
						return false;
					}

			if (SymbolTable.GetCSType(parent) == null)
				return false;

			return true;
		}
		
		protected override void GenCtors (StreamWriter sw)
		{
			if (!Elem.HasAttribute("parent"))
				return;

			sw.WriteLine("\t\t~" + Name + "()");
			sw.WriteLine("\t\t{");
			sw.WriteLine("\t\t\tDispose();");
			sw.WriteLine("\t\t}");
			sw.WriteLine();
			sw.WriteLine("\t\tprotected " + Name + "(uint gtype) : base(gtype) {}");
			sw.WriteLine("\t\tpublic " + Name + "(IntPtr raw) : base(raw) {}");
			sw.WriteLine();

			base.GenCtors (sw);
		}

		/* Keep this in sync with the one in glib/ObjectManager.cs */
		static string GetExpected (string cname)
		{
			StringBuilder expected = new StringBuilder ();
			string ns = "";
			bool needs_dot = true;
			for (int i = 0; i < cname.Length; i++)
			{
				if (needs_dot && i > 0 && Char.IsUpper (cname[i])) {
					ns = expected.ToString ().ToLower (); 
					expected.Append ('.');
					needs_dot = false;
				}
				expected.Append (cname[i]);
			}
			expected.AppendFormat (",{0}-sharp", ns);
			return expected.ToString ();
		}

		public static void GenerateMapper ()
		{
			foreach (string ns in namespaces.Keys) {
				Hashtable objs = (Hashtable) namespaces[ns];
				bool needs_map = false;
				foreach (string key in objs.Keys) {
					string expected = GetExpected (key);
					if (expected != ((string) objs[key])) {
						needs_map = true;
					}
				}

				if (!needs_map)
					continue;
	
				char sep = Path.DirectorySeparatorChar;
				string dir = ".." + sep + ns.ToLower () + sep + "generated";
				if (!Directory.Exists(dir)) {
        			Console.WriteLine ("creating " + dir);
      		  	Directory.CreateDirectory(dir);
				}
				String filename = dir + sep + "ObjectManager.cs";

				FileStream stream = new FileStream (filename, FileMode.Create, FileAccess.Write);
				StreamWriter sw = new StreamWriter (stream);

				sw.WriteLine ("// Generated File.  Do not modify.");
				sw.WriteLine ("// <c> 2001-2002 Mike Kestner");
				sw.WriteLine ();

				sw.WriteLine ("namespace GtkSharp {");
				sw.WriteLine ();
				sw.WriteLine ("\tnamespace " + ns + " {");
				sw.WriteLine ();
				sw.WriteLine ("\tpublic class ObjectManager {");
				sw.WriteLine ();
				sw.WriteLine ("\t\t// Call this method from the appropriate module init function.");
				sw.WriteLine ("\t\tpublic static void Initialize ()");
				sw.WriteLine ("\t\t{");
	
				foreach (string key in objs.Keys) {
					string expected = GetExpected (key);
					if (expected != ((string) objs[key])) {
						sw.WriteLine ("\t\t\tGtkSharp.ObjectManager.RegisterType(\"" + key + "\", \"" + objs[key] + "\");");
					}
				}
					
				sw.WriteLine ("\t\t}");
				sw.WriteLine ("\t}");
				sw.WriteLine ("}");
				sw.WriteLine ("}");

				sw.Flush ();
				sw.Close ();
			}
		}
	}
}

