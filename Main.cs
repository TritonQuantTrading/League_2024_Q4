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
using System.Security.Cryptography.X509Certificates;
using QLNet;
using Accord.Math;
using Accord.Math.Optimization;
using Accord.Statistics;
using Accord;
using Fasterflect;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
#endregion
namespace QuantConnect.Algorithm.CSharp
{
   
    // Check out the overview of QCAlgorithm:
    // https://www.quantconnect.com/docs/v2/writing-algorithms/key-concepts/algorithm-engine#03-Your-Algorithm-and-LEAN
    public class League2024Q4 : QCAlgorithm
    {
        // Public fields
        public const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        public const decimal InitialCash = 1_000_000;
        public readonly (DateTime, DateTime)[] TestPeriods = new[]
        {                                                         //[i]  [N] years (start => end, drawdown < -20%)
            (new DateTime(2019, 1, 1), new DateTime(2024, 6, 1)), //[0]  5 years   (2020-01-20 => 2020-04-04, -41.33%)
            (new DateTime(2014, 1, 1), new DateTime(2024, 6, 1)), //[1]  10 years  (2020-01-20 => 2020-04-04, -41.33%)
            (new DateTime(2014, 1, 1), new DateTime(2019, 6, 1)), //[2]  5 years
            (new DateTime(2009, 1, 1), new DateTime(2014, 6, 1)), //[3]  5 years   (2010-03-16 => 2012-01-31, -27.54%)
            (new DateTime(2004, 1, 1), new DateTime(2009, 6, 1)), //[4]  5 years   (2007-07-28 => 2009-07-07, -78.28%)
            (new DateTime(1999, 1, 1), new DateTime(2004, 6, 1)), //[5]  5 years   (1999-03-01 => 2003-03-13, -59.88%)
            (new DateTime(1999, 1, 1), new DateTime(2009, 6, 1)), //[6]  10 years
            (new DateTime(2004, 1, 1), new DateTime(2014, 6, 1)), //[7]  10 years
            (new DateTime(2009, 1, 1), new DateTime(2019, 6, 1)), //[8]  10 years
            (new DateTime(1999, 1, 1), new DateTime(2014, 6, 1)), //[9]  15 years
            (new DateTime(2004, 1, 1), new DateTime(2019, 6, 1)), //[10] 15 years
            (new DateTime(2009, 1, 1), new DateTime(2024, 6, 1)), //[11] 15 years
            (new DateTime(1999, 1, 1), new DateTime(2019, 6, 1)), //[12] 20 years
            (new DateTime(2004, 1, 1), new DateTime(2024, 6, 1)), //[13] 20 years
            (new DateTime(1999, 1, 1), new DateTime(2024, 6, 1)), //[14] 25 years
            (new DateTime(1998, 1, 1), new DateTime(2016, 6, 1)), //[15] 18 years  (1999-11-28 => 2003-02-28, -66.10%) (2007-07-12 => 2009-03-12, -61.17%)
        };
        /* Events Identified:
         1. 2020-01-20 to 2020-04-04 (-41.33%)
            *COVID-19 Pandemic*
            - This period corresponds to the global stock market crash caused by the COVID-19 pandemic.
            Beginning in late January 2020, as the virus spread and lockdown measures were enforced globally, 
            the financial markets experienced extreme volatility. The S&P 500 and other major indexes saw 
            sharp declines, with March 2020 being particularly devastating. Central banks responded with 
            aggressive monetary policy measures, which helped stabilize markets in the second quarter of 2020.

         2. 1999-11-28 to 2003-02-28 (-66.10%)
            *Dot-Com Bubble Burst*
            The period from late 1999 to early 2003 marks the collapse of the dot-com bubble.
            - Many technology stocks were vastly overvalued in the late 1990s, fueled by speculative investments 
            in internet-based companies. The bubble burst in early 2000, and stock prices for technology companies
            plummeted. The NASDAQ Composite Index, which included many of these companies, lost nearly 78% of its 
            value from its peak in March 2000 to its trough in October 2002. The crash was compounded by economic 
            slowdowns and the 9/11 attacks in 2001.

         3. 2007-07-12 to 2009-03-12 (-61.17%)
            *Global Financial Crisis (GFC)*
            This period corresponds to the global financial crisis, which began in mid-2007 and reached
            its peak in late 2008 and early 2009.
            - The collapse of major financial institutions, the bursting of the U.S. housing bubble, and the resulting
            credit crisis led to a severe global recession. Stock markets around the world saw dramatic declines, 
            with the S&P 500 losing more than half its value from October 2007 to March 2009. Central banks and 
            governments worldwide implemented massive stimulus packages to prevent further economic collapse.
        */
        public const int PeriodIndex = 0;
        public override void Initialize()
        
        {
            /*************** Start Default Initialization *****************/
            // Set Dates (will be ignored in live mode)
            SetStartDate(TestPeriods[PeriodIndex].Item1);
            SetEndDate(TestPeriods[PeriodIndex].Item2);

            // Set Account Currency
            // - Default is USD $100,000
            // - Must be done before SetCash()
            // SetAccountCurrency("USD");
            // SetAccountCurrency("BTC", 10);
            //
            // Question: How to set multiple currencies? For example, if you want to use a mix of USD and BTC
            // Answer: No, you cannot set multiple account currency and you can only set it once. Its internal valule is used for all calculations.
            // Reference: https://www.quantconnect.com/docs/v2/writing-algorithms/portfolio/cashbook#02-Account-Currency

            // Set Cash
            // SetCash("BTC", 10);
            SetCash(InitialCash);
            /***************** End Default Initialization *****************/

            // Docs:
            // Universe.Members: When you remove an asset from a universe, LEAN usually removes the security from the Members collection and removes the security subscription.
            // ActiveSecurities: When you remove an asset from a universe, LEAN usually removes the security from the ActiveSecurities collection and removes the security subscription.
            //
            // Question: What's the differences between `Universe.Members` and `Universe.ActiveSecurities`?
            // Answer: `ActiveSecurities` is a collection of all `Members` from all universes.
            //
            // UniverseManager[_universe.Configuration.Symbol].Members:
            // FIXME: Multiple universes are allowed? But the members are only for certain unvierse, but the active securities are for all universes
            // FIXME: what is the Symbol of a universe? Where is it defined?
            // Note: both `UniverseManager` and `ActiveSecurities` are properties of the `QCAlgorithm` class
            // To have access to all securities without considering they are activthee or not, use `Securities` property
            // - There are still cases where the Securities may remove the security, but only from the primary collection (Securities.Values), and can still be accessed from Securities.Total
            //
            // Universe.Selected: Different from Members, Members may contain more assets
            // Diffs Remarks:
            //   This set might be different than QuantConnect.Data.UniverseSelection.Universe.Securities
            //   which might hold members that are no longer selected but have not been removed
            //   yet, this can be because they have some open position, orders, haven't completed
            //   the minimum time in universe

            /***************** Start Algorithm Framework ******************/
            // Set Universe Settings
            UniverseSettings.Resolution = Resolution.Daily;
            UniverseSettings.DataNormalizationMode = DataNormalizationMode.Raw; // allow options
            UniverseSettings.Schedule.On(DateRules.MonthStart());
            // UniverseSettings.Asynchronous = true; // This would cause backtest consistency issues, see: https://www.quantconnect.com/docs/v2/writing-algorithms/algorithm-framework/universe-selection/universe-settings#09-Asynchronous-Selection
            // UniverseSettings.ExtendedMarketHours = true; // only set to true if you are performing intraday trading
            // AddUniverseSelection(new FundamentalUniverseSelectionModel(Select, UniverseSettings));

            // Set Security Initializer
            // - This allow any custom security-level settings, instead of using the global universe settings
            // - SetSecurityInitializer(security => security.SetFeeModel(new ConstantFeeModel(0, "USD")));
            // - SetSecurityInitializer(new MySecurityInitializer(BrokerageModel, new FuncSecuritySeeder(GetLastKnownPrices)));
            SetSecurityInitializer(new BrokerageModelSecurityInitializer(
                this.BrokerageModel, new FuncSecuritySeeder(this.GetLastKnownPrices)
            ));
            // Set Selection
            AddUniverseSelection(new BasicUniverseSelectionModel());
            // var _optionFilter = new Func<OptionFilterUniverse, OptionFilterUniverse>(universe =>
            // {
            //     return universe
            //         .Strikes(-10, -10)
            //         .Expiration(TimeSpan.FromDays(45), TimeSpan.FromDays(60));
            // });
            // AddUniverseSelection(new DerivedOptionsUniverseSelectionModel(
            //     new BasicUniverseSelectionModel(), _optionFilter
            // )); // Don't need to add the basic universe selection model in advance since it is handled internally
            // Set Alphas
            AddAlpha(new TempAlphaModel());
            // Set Portfolio
            Settings.RebalancePortfolioOnInsightChanges = false;
            Settings.RebalancePortfolioOnSecurityChanges = false;
            SetPortfolioConstruction(new MomentumPortfolioConstructionModel());
            // Set Risk 
            // Set Execution
            SetExecution(new ImmediateExecutionModel());
            /****************** End Algorithm Framework *******************/

            // Set Warmup Period
            // SetWarmUp(PLookback/2, Resolution.Daily);
        }
        public override void OnWarmupFinished()
        {
            // OnWarmupFinished() is the last method called before the algorithm starts running
            // - You can notify yourself by overriding this method: public override void OnWarmupFinished() { Log("Algorithm Ready"); }
            // - You can train machine learning models here: public override void OnWarmupFinished() { Train(MyTrainingMethod); }
            // The OnWarmupFinished() will be called after the warmup period even if the warmup period is not set
            Log("Algorithm Ready");
            // show universities
            // foreach (var universe in UniverseManager.Values)
            // {
            //     Log($"Init Universe: {universe.Configuration.Symbol}: {universe.Members.Count} members");
            // }
            // PostInitialize() method should never be overridden because it is used for predefined post-initialization routines
        }

        // public override void OnData(Slice slice)
        // {
        //     Log($"OnData: {Time}, {slice.Keys.Count} symbols, {slice.Bars.Count} bars");
        //     The suggested way of handling the time-based event is using the Scheduled Events 
        //     instead of checking the time in the OnData method
        //     Source: https://www.quantconnect.com/docs/v2/writing-algorithms/scheduled-events
        //
        //     Scheduled Events let you trigger code to run at specific times of day, regardless of
        //     your algorithm's data subscriptions. It's easier and more reliable to execute
        //     time-based events with Scheduled Events than checking the current algorithm time in
        //     the OnData event handler.
        // }
    }
}