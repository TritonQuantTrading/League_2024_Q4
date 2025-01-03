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
using Ionic.Zip;
using QuantConnect.Algorithm.Framework.Alphas.Analysis;
using Accord;
using QLNet;
using Accord.Math;
#endregion

namespace QuantConnect {
    public class ProtectivePutModel : RiskManagementModel {
        private readonly decimal _putStrikePercent = 0.95m;    // 5% OTM
        private readonly int _daysToExpiration = 45;           // 45天到期
        private readonly decimal _hedgeRatio = 1.0m;           // 完全对冲
        private Dictionary<Symbol, Symbol> _optionSymbols;     // 追踪每个股票对应的期权
        
        public ProtectivePutModel() {
            _optionSymbols = new Dictionary<Symbol, Symbol>();
        }
        
        public override IEnumerable<IPortfolioTarget> ManageRisk(
            QCAlgorithm algorithm, 
            IPortfolioTarget[] targets) {
            
            var riskAdjustedTargets = new List<IPortfolioTarget>();
            
            // 检查所有股票持仓
            foreach(var kvp in algorithm.Portfolio) {
                var equity = kvp.Value;
                if(!equity.Invested || equity.Symbol.SecurityType != SecurityType.Equity) continue;
                
                // 计算需要的put数量
                var quantity = Math.Abs(equity.Quantity);
                if(quantity == 0) continue;
                
                // 确保我们有对应的期权链
                var chain = algorithm.OptionChainProvider.GetOptionContractList(equity.Symbol, algorithm.Time);
                if(chain == null || !chain.Any()) continue;
                
                // 计算目标strike price
                var currentPrice = equity.Price;
                var targetStrike = currentPrice * _putStrikePercent;
                
                // 寻找最接近目标到期日和strike的put
                var targetExpiry = algorithm.Time.AddDays(_daysToExpiration);
                var option = chain
                    .Where(x => x.ID.OptionRight == OptionRight.Put)
                    .Where(x => x.ID.Date >= targetExpiry)
                    .OrderBy(x => Math.Abs((x.ID.Date - targetExpiry).TotalDays))
                    .ThenBy(x => Math.Abs(x.ID.StrikePrice - targetStrike))
                    .FirstOrDefault();
                
                if(option == null) continue;
                
                // 如果已经有对应的put，检查是否需要更新
                if(_optionSymbols.TryGetValue(equity.Symbol, out var existingOption)) {
                    var existingPosition = algorithm.Portfolio[existingOption];
                    // 如果已有合适的对冲，跳过
                    if(existingPosition.Invested && 
                       existingPosition.Quantity == quantity * _hedgeRatio) continue;
                    
                    // 清掉旧的put
                    riskAdjustedTargets.Add(PortfolioTarget.Percent(existingOption, 0));
                }
                
                // 购买新的put
                var putQuantity = (int)(quantity * _hedgeRatio);
                if(putQuantity > 0) {
                    _optionSymbols[equity.Symbol] = option;
                    riskAdjustedTargets.Add(PortfolioTarget.Quantity(option, putQuantity));
                    algorithm.Debug($"Adding protective put for {equity.Symbol}: {putQuantity} contracts of {option}");
                }
            }
            
            return riskAdjustedTargets;
        }
    }
}