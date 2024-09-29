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
using Accord.Math;
using MathNet.Numerics.LinearAlgebra;
#endregion
namespace QuantConnect
{
    /// <summary>
    /// A Monte Carlo-based portfolio optimizer that searches for the portfolio with the best Sortino ratio
    /// </summary>
    public class SortinoEfficientFrontierOptimizer : IPortfolioOptimizer
    {
        private readonly int _numPortfolios;
        private readonly int _lookback;
        private readonly int _randomSeed;

        /// <summary>
        /// Initializes a new instance of <see cref="SortinoEfficientFrontierOptimizer"/>
        /// </summary>
        /// <param name="numPortfolios">Number of portfolios to generate in the Monte Carlo simulation</param>
        /// <param name="lookback">Lookback period for historical returns</param>
        /// <param name="randomSeed">Seed for the random number generator</param>
        public SortinoEfficientFrontierOptimizer(int numPortfolios = 1000, int lookback = 63, int randomSeed = 11)
        {
            _numPortfolios = numPortfolios;
            _lookback = lookback;
            _randomSeed = randomSeed;
        }

        /// <summary>
        /// Perform portfolio optimization using Monte Carlo simulation
        /// </summary>
        /// <param name="historicalReturns">Matrix of historical returns (rows: observations, columns: assets)</param>
        /// <param name="expectedReturns">Array of expected returns (not used in this optimizer)</param>
        /// <param name="covariance">Covariance matrix (not used here as we compute it internally)</param>
        /// <returns>Array of portfolio weights</returns>
        public double[] Optimize(double[,] historicalReturns, double[] expectedReturns = null, double[,] covariance = null)
        {
            int nObservations = historicalReturns.GetLength(0); // Number of time periods
            int nAssets = historicalReturns.GetLength(1);       // Number of assets

            if (nAssets == 0)
            {
                Console.WriteLine("[OptimizePortfolio] No assets provided. Returning empty weights.");
                return new double[0];
            }

            // Convert historicalReturns to a matrix for easier computations
            var returnsMatrix = Matrix<double>.Build.DenseOfArray(historicalReturns).Transpose();
            // Now, returnsMatrix rows correspond to assets, and columns correspond to observations

            // Calculate mean returns for each asset
            var meanReturns = new double[nAssets];
            for (int i = 0; i < nAssets; i++)
            {
                meanReturns[i] = returnsMatrix.Row(i).Average();
            }

            // Calculate covariance matrix (assets x assets)
            var covarianceMatrix = returnsMatrix * returnsMatrix.Transpose() / (nObservations - 1);

            var portfolioReturns = new double[_numPortfolios];
            var sortinoRatios = new double[_numPortfolios];
            var weightsRecord = new List<double[]>(_numPortfolios);

            var random = new Random(_randomSeed);

            for (int i = 0; i < _numPortfolios; i++)
            {
                // Generate random weights and normalize
                var weights = new double[nAssets];
                double sumWeights = 0.0;
                for (int j = 0; j < nAssets; j++)
                {
                    double w = random.NextDouble();
                    weights[j] = w;
                    sumWeights += w;
                }
                for (int j = 0; j < nAssets; j++)
                {
                    weights[j] /= sumWeights;
                }

                var weightsVector = Vector<double>.Build.DenseOfArray(weights);

                // Calculate portfolio return (scaled by lookback period)
                double portfolioReturn = 0.0;
                for (int j = 0; j < nAssets; j++)
                {
                    portfolioReturn += meanReturns[j] * weights[j];
                }
                portfolioReturn *= _lookback;

                // Calculate downside risk (downside standard deviation)
                double downsideSum = 0.0;
                for (int j = 0; j < nAssets; j++)
                {
                    var assetReturns = returnsMatrix.Row(j);
                    foreach (var r in assetReturns)
                    {
                        if (r < 0)
                        {
                            downsideSum += Math.Pow(r, 2) * weights[j];
                        }
                    }
                }
                double downsideStdDev = Math.Sqrt(downsideSum / nObservations);

                // Calculate Sortino ratio
                double sortinoRatio = downsideStdDev > 0 ? portfolioReturn / downsideStdDev : 0;

                // Store results
                portfolioReturns[i] = portfolioReturn;
                sortinoRatios[i] = sortinoRatio;
                weightsRecord.Add(weights);
            }

            // Select the portfolio with the highest Sortino ratio
            int bestSortinoIndex = Array.IndexOf(sortinoRatios, sortinoRatios.Max());

            if (bestSortinoIndex < 0 || bestSortinoIndex >= weightsRecord.Count)
            {
                Console.WriteLine("[OptimizePortfolio] Unable to determine the best Sortino index. Returning equal weights.");
                var equalWeights = Enumerable.Repeat(1.0 / nAssets, nAssets).ToArray();
                return equalWeights;
            }

            return weightsRecord[bestSortinoIndex];
        }
    }
}
