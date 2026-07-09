using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Acornima.Ast;
using Jint;

namespace CJintLib {
	public class JsApp : JsBase {
		public delegate JsLib ImportLibsProc(params string[] names);

		public JsLib importLibs(params string[] names) {
			var eng = Engine;
			var res = JsLib.From(names);
			if (res == null || !res.fill(JsLib.JintRoot))
				throw new FileLoadException($"failed to load libs: {string.Join(" | ", names)}");

			res?.evalAsync(eng);
			// eng.Con.debug($"imported: {string.Join(" | ", names)}");
			return res;
		}
	}
}
