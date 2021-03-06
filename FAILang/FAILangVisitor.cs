﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FAILang.Types;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using FAILang.Types.Unevaluated;
using System.Numerics;

namespace FAILang
{
    public class FAILangVisitor : FAILangBaseVisitor<IType>
    {
        public new IType[] VisitCalls([NotNull] FAILangParser.CallsContext context)
        {
            return context.call().Select(call => VisitCall(call)).ToArray();
        }

        public override IType VisitCall([NotNull] FAILangParser.CallContext context)
        {
            if (context.def() != null)
            {
                return VisitDef(context.def());
            }
            if (context.expression() != null)
                return VisitExpression(context.expression());
            return new Error("ParseError", "The input failed to parse.");
        }

        public override IType VisitDef([NotNull] FAILangParser.DefContext context)
        {
            var name = context.name().GetText();
            var update = context.update;
            var exp = context.expression();

            if (Global.reservedNames.Contains(name))
                return new Error("DefineFailed", $"{name} is a reserved name.");
            if (update == null && Global.globalVariables.ContainsKey(name))
                return new Error("DefineFailed", "The update keyword is required to change a function or variable.");

            bool memoize = context.memoize != null;

            // `update memo name` pattern
            if (exp == null && memoize)
            {
                if (!Global.globalVariables.ContainsKey(name))
                    return new Error("UpdateFailed", $"{name} is not defined");
                if (Global.globalVariables.TryGetValue(name, out var val) && val is Function func)
                {
                    if (func.memoize)
                        func.memos.Clear();
                    else
                        func.memoize = true;
                }
                else
                    return new Error("UpdateFailed", $"{name} is not a function");
            }
            else if (context.fparams() != null)
            {
                IType expr = VisitExpression(exp);
                Function f = new Function(context.fparams().param().Select(x => x.GetText()).ToArray(), expr, memoize: memoize, elipsis: context.fparams().elipsis != null);

                Global.globalVariables[name] = f;
            }
            else
            {
                var v = Global.Evaluate(VisitExpression(exp));
                if (v is Error)
                    return v;
                Global.globalVariables[name] = v;
            }

            return null;
        }

        public override IType VisitExpression([NotNull] FAILangParser.ExpressionContext context)
        {
            var type = context.type();
            var name = context.name();
            var op = context.op;
            var prefix = context.prefix();
            var cond = context.cond();
            var callparams = context.callparams();
            var lambda = context.lambda();
            var union = context.union();
            var indexer = context.indexer();

            if      (type != null)
                return VisitType(type);
            else if (name != null)
                return VisitName(name);
            else if (op != null)
            {
                var exprs = context.expression();
                Operator oper = Operator.MULTIPLY;
                switch (op.Text)
                {
                    case "+":
                        oper = Operator.ADD;
                        break;
                    case "-":
                        oper = Operator.SUBTRACT;
                        break;
                    case "*":
                        oper = Operator.MULTIPLY;
                        break;
                    case "":
                        oper = Operator.MULTIPLY;
                        break;
                    case "/":
                        oper = Operator.DIVIDE;
                        break;
                    case "%":
                        oper = Operator.MODULO;
                        break;
                    case "^":
                        oper = Operator.EXPONENT;
                        break;
                    case "=":
                        oper = Operator.EQUALS;
                        break;
                    case "~=":
                        oper = Operator.NOT_EQUALS;
                        break;
                    case ">":
                        oper = Operator.GREATER;
                        break;
                    case "<":
                        oper = Operator.LESS;
                        break;
                    case ">=":
                        oper = Operator.GR_EQUAL;
                        break;
                    case "<=":
                        oper = Operator.LE_EQUAL;
                        break;
                }
                return new OperatorExpression(oper, VisitExpression(exprs[0]), VisitExpression(exprs[1]));
            }
            else if (prefix != null)    
            {
                Prefix pre = Prefix.NOT;
                switch (prefix.GetText())
                {
                    case "~":
                        pre = Prefix.NOT;
                        break;
                    case "-":
                        pre = Prefix.NEGATIVE;
                        break;
                }
                return new PrefixExpression(pre, VisitExpression(context.expression(0)));
            }
            else if (cond != null)      return VisitCond(cond);
            else if (callparams != null)    
            {
                return new FunctionExpression(VisitExpression(context.expression(0)),
                    callparams.arg()
                        .Select(x => (VisitExpression(x.expression()), x.elipsis != null)).ToArray());
            }
            else if (lambda != null)    return VisitLambda(lambda);
            else if (union != null)     return VisitUnion(union);
            else if (indexer != null)   
            {
                IType l_expr = null;
                if (indexer.l_index != null)
                    l_expr = VisitExpression(indexer.l_index);
                IType r_expr = null;
                if (indexer.r_index != null)
                    r_expr = VisitExpression(indexer.r_index);
                return new IndexerExpression(VisitExpression(context.expression(0)), l_expr, r_expr, indexer.elipsis != null);
            }

            return VisitExpression(context.expression(0));
        }

        public override IType VisitName([NotNull] FAILangParser.NameContext context) =>
            new NamedArgument(context.GetText());

        public override IType VisitType([NotNull] FAILangParser.TypeContext context)
        {
            var number = context.t_number;
            var str = context.t_string;
            var boolean = context.t_boolean;
            var void_type = context.t_void;
            var vector = context.vector();
            var tuple = context.tuple();

            if (number != null)
            {
                if (number.Text.Equals("i"))
                    return new Number(Complex.ImaginaryOne);
                else if (number.Text.EndsWith('i'))
                    return new Number(Complex.ImaginaryOne * Convert.ToDouble(number.Text.TrimEnd('i')));
                else
                    return new Number(Convert.ToDouble(number.Text));
            }
            else if (str != null)
            {
                string processed = str.Text.Substring(1, str.Text.Length - 2)
                    .Replace("\\\\", "\\")
                    .Replace("\\b", "\b")
                    .Replace("\\f", "\f")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\v", "\v")
                    .Replace("\\\"", "\"");
                return new MathString(processed);
            }
            else if (boolean != null)
                return boolean.Text.Equals("true") ? MathBool.TRUE : MathBool.FALSE;
            else if (vector != null)
                return VisitVector(vector);
            else if (tuple != null)
                return VisitTuple(tuple);
            else
                return Types.Void.instance;
        }

        public override IType VisitVector([NotNull] FAILangParser.VectorContext context) =>
            new UnevaluatedVector(context.expression().Select(x => VisitExpression(x)).ToArray());

        public override IType VisitTuple([NotNull] FAILangParser.TupleContext context) =>
            new UnevaluatedTuple(context.expression().Select(x => VisitExpression(x)).ToArray());

        public override IType VisitCond([NotNull] FAILangParser.CondContext context)
        {
            var conditions = context.condition();
            IType[] conds = new IType[conditions.Length];
            IType[] exprs = new IType[conditions.Length];
            for (int i = 0; i < conditions.Length; i++)
            {
                conds[i] = VisitExpression(conditions[i].expression(0));
                exprs[i] = VisitExpression(conditions[i].expression(1));
            }
            return new CondExpression(conds, exprs, VisitExpression(context.expression()));
        }

        public override IType VisitLambda([NotNull] FAILangParser.LambdaContext context)
        {
            if (context.fparams() != null)
                return new Function(context.fparams().param().Select(x => x.GetText()).ToArray(), VisitExpression(context.expression()), memoize: context.memoize != null, elipsis: context.fparams().elipsis != null);
            else
                return new Function(new string[] { context.param().GetText() }, VisitExpression(context.expression()), memoize: false, elipsis: context.elipsis != null);
        }

        public override IType VisitUnion([NotNull] FAILangParser.UnionContext context)
        {
            var exprs = context.expression();
            IType[] vals = new IType[exprs.Length];
            for (int i = 0; i < exprs.Length; i++)
                vals[i] = VisitExpression(exprs[i]);
            return new Union(vals);
        }
    }
}
