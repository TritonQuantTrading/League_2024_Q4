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
using Accord.Math.Optimization;
using Accord.Statistics;
using Accord.Math;
#endregion

namespace QuantConnect {
   /// <summary>
    /// A Quadratic Programming-based portfolio optimizer that minimizes risk (variance)
    /// while ensuring weights sum to 1 and are bounded between 0 and 1.
    /// </summary>
    public class QuadraticProgrammingPortfolioOptimizer : IPortfolioOptimizer
    {
        private readonly int _shortLookback;

        /// <summary>
        /// Initializes a new instance of <see cref="QuadraticProgrammingPortfolioOptimizer"/>
        /// </summary>
        /// <param name="shortLookback">The number of days to use for the lookback period</param>
        public QuadraticProgrammingPortfolioOptimizer(int shortLookback = 63)
        {
            _shortLookback = shortLookback;
        }

        /// <summary>
        /// Perform portfolio optimization using Quadratic Programming
        /// </summary>
        /// <param name="historicalReturns">Matrix of historical returns (K x N)</param>
        /// <param name="expectedReturns">Array of expected returns (K x 1)</param>
        /// <param name="covariance">Covariance matrix (K x K)</param>
        /// <returns>Array of portfolio weights (K x 1)</returns>
        public double[] Optimize(double[,] historicalReturns, double[] expectedReturns = null, double[,] covariance = null)
        {
            var nAssets = historicalReturns.GetLength(1);

            // Calculate the covariance matrix if not provided
            covariance ??= historicalReturns.Covariance();
            
            // Calculate average returns if not provided
            var meanReturns = expectedReturns ?? historicalReturns.Mean(0);

            // Step 1: Setup QP Problem to Minimize Portfolio Variance
            var optfunc = new QuadraticObjectiveFunction(covariance, Accord.Math.Vector.Create(nAssets, 0.0));

            // Constraints: weights sum to 1 (budget constraint)
            var constraints = new List<LinearConstraint>
            {
                new LinearConstraint(nAssets)
                {
                    CombinedAs = Accord.Math.Vector.Create(nAssets, 1.0),
                    ShouldBe = ConstraintType.EqualTo,
                    Value = 1.0
                }
            };

            // Boundary conditions: Weights should be between 0 and 1 (no short selling)
            for (int i = 0; i < nAssets; i++)
            {
                constraints.Add(new LinearConstraint(1)
                {
                    VariablesAtIndices = new[] { i },
                    ShouldBe = ConstraintType.GreaterThanOrEqualTo,
                    Value = 0.0 // Lower bound
                });
                constraints.Add(new LinearConstraint(1)
                {
                    VariablesAtIndices = new[] { i },
                    ShouldBe = ConstraintType.LesserThanOrEqualTo,
                    Value = 1.0 // Upper bound
                });
            }

            // Step 2: Solve the optimization problem using Goldfarb-Idnani QP solver
            var solver = new GoldfarbIdnani(optfunc, constraints);
            var x0 = Accord.Math.Vector.Create(nAssets, 1.0 / nAssets); // Starting with equal weights
            var success = solver.Minimize(Accord.Math.Vector.Copy(x0));

            if (!success)
            {
                Console.WriteLine("[OptimizePortfolio] Quadratic programming optimization failed. Returning equal weights.");
                return Enumerable.Repeat(1.0 / nAssets, nAssets).ToArray();
            }

            // Step 3: Extract the optimal solution
            var solution = solver.Solution
                .Select(x => x.IsNaNOrInfinity() ? 0 : x).ToArray();

            // Scale the solution to ensure that the sum of the weights is 1
            var sumOfWeights = solution.Sum();
            if (sumOfWeights.IsNaNOrZero())
            {
                Console.WriteLine("[OptimizePortfolio] Invalid solution from QP optimizer. Returning equal weights.");
                return Enumerable.Repeat(1.0 / nAssets, nAssets).ToArray();
            }

            // Return the scaled portfolio weights
            return solution.Divide(sumOfWeights);
        }
    } 
}