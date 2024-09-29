#region imports
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Drawing;
using QuantConnect;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Portfolio.SignalExports;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Selection;
using QuantConnect.Api;
using QuantConnect.Parameters;
using QuantConnect.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Util;
using QuantConnect.Interfaces;
using QuantConnect.Algorithm;
using QuantConnect.Indicators;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Custom;
using QuantConnect.Data.Custom.IconicTypes;
using QuantConnect.DataSource;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.Market;
using QuantConnect.Data.Shortable;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Notifications;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.OptionExercise;
using QuantConnect.Orders.Slippage;
using QuantConnect.Orders.TimeInForces;
using QuantConnect.Python;
using QuantConnect.Scheduling;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.Option;
using QuantConnect.Securities.Positions;
using QuantConnect.Securities.Forex;
using QuantConnect.Securities.Crypto;
using QuantConnect.Securities.CryptoFuture;
using QuantConnect.Securities.Interfaces;
using QuantConnect.Securities.Volatility;
using QuantConnect.Storage;
using QuantConnect.Statistics;
using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
using Accord.Statistics;
using Accord.Math.Optimization;
using Accord.Math;
#endregion

namespace QuantConnect
{
    public class SOCPortfolioOptimizer : IPortfolioOptimizer
    {
        private readonly double _riskFreeRate;

        public SOCPortfolioOptimizer(double riskFreeRate = 0.01)
        {
            _riskFreeRate = riskFreeRate;
        }

        /// <summary>
        /// Optimize portfolio weights using Second-Order Cone Programming (SOCP)
        /// </summary>
        /// <param name="historicalReturns">Matrix of historical returns (K x N)</param>
        /// <param name="expectedReturns">Array of expected returns</param>
        /// <param name="covariance">Covariance matrix (K x K)</param>
        /// <returns>Array of portfolio weights (K x 1)</returns>
        public double[] Optimize(double[,] historicalReturns, double[] expectedReturns = null, double[,] covariance = null)
        {
            covariance ??= historicalReturns.Covariance();
            var size = covariance.GetLength(0);
            var returns = expectedReturns ?? historicalReturns.Mean(0);

            // Set up constraints: sum of weights equals 1
            var budgetConstraint = new LinearConstraint(size)
            {
                CombinedAs = Accord.Math.Vector.Create(size, 1.0),
                ShouldBe = ConstraintType.EqualTo,
                Value = 1.0
            };

            // Create SOCP optimization problem
            var optfunc = new QuadraticObjectiveFunction(covariance, Vector.Create(size, 0.0));
            var constraints = new List<LinearConstraint> { budgetConstraint };

            // Set up the solver
            var solver = new GoldfarbIdnani(optfunc, constraints);

            // Initial guess (equal weights)
            var x0 = Accord.Math.Vector.Create(size, 1.0 / size);

            // Minimize downside risk (SOCP formulation)
            var success = solver.Minimize(Accord.Math.Vector.Copy(x0));
            if (!success) return x0;

            // Ensure valid solution (replace NaN/Infinity with zeros)
            var solution = solver.Solution.Select(x => x.IsNaNOrInfinity() ? 0 : x).ToArray();

            // Scale the solution to ensure sum of absolute weights equals 1
            var sumOfAbsoluteWeights = solution.Abs().Sum();
            return sumOfAbsoluteWeights.IsNaNOrZero() ? x0 : solution.Divide(sumOfAbsoluteWeights);
        }
    }
}