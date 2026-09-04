using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OCPP.Core.Database;
using OCPP.Core.Management.Models;

namespace OCPP.Core.Management.Controllers
{
    public partial class HomeController : BaseController
    {
        /// <summary>
        /// Monthly Charge Report: energy DRAWN from supply per charge point over a
        /// calendar month, using a telescoping meter-delta so that between-session
        /// idle draw is included and every kWh is attributed to exactly one month.
        ///
        /// Rule per charge point, for the reported month:
        ///   energy = (start-meter of the first session that starts AFTER the month)
        ///            - (start-meter of the first session that starts IN the month)
        ///   Fallback (no session after the month yet): use the last completed
        ///   session's stop-meter as the closing anchor.
        ///   "session active": if the closing anchor would be an OPEN (unfinished)
        ///   session that started within the reported month, no reliable number
        ///   exists -> show "session active" instead of kWh.
        /// </summary>
        [Authorize]
        public IActionResult MonthlyChargeReport(int? year, int? month)
        {
            try
            {
                Logger.LogTrace("MonthlyChargeReport: year={0} month={1}", year, month);

                // Default: previous month
                DateTime firstOfThisMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                DateTime defaultMonth = firstOfThisMonth.AddMonths(-1);

                int y = year ?? defaultMonth.Year;
                int m = month ?? defaultMonth.Month;
                // Normalise (guards against out-of-range from manual URL editing)
                DateTime monthStartLocal;
                try
                {
                    monthStartLocal = new DateTime(y, m, 1);
                }
                catch
                {
                    monthStartLocal = defaultMonth;
                    y = defaultMonth.Year;
                    m = defaultMonth.Month;
                }
                DateTime monthEndLocalExclusive = monthStartLocal.AddMonths(1); // first day of NEXT month (exclusive)

                // DB timestamps are UTC
                DateTime periodStartUtc = monthStartLocal.ToUniversalTime();
                DateTime periodEndUtc = monthEndLocalExclusive.ToUniversalTime();

                var vm = new MonthlyChargeReportViewModel
                {
                    StartDate = monthStartLocal,
                    StopDate = monthEndLocalExclusive.AddDays(-1), // last day of reported month
                    Year = y,
                    Month = m,
                    Rows = new List<MonthlyChargeReportRow>()
                };

                // Charge point display names
                var cpNames = DbContext.ChargePoints
                    .AsNoTracking()
                    .ToDictionary(cp => cp.ChargePointId, cp => cp.Name);

                // Charge tag display names
                var tagNames = DbContext.ChargeTags
                    .AsNoTracking()
                    .ToDictionary(ct => ct.TagId, ct => ct.TagName);

                // All transactions, ordered — we need sessions after the period too (telescoping anchor).
                // Keep it modest: load all and group in memory (fine for a home/small install).
                var allTx = DbContext.Transactions
                    .AsNoTracking()
                    .OrderBy(t => t.ChargePointId)
                    .ThenBy(t => t.StartTime)
                    .ToList();

                foreach (var cpGroup in allTx.GroupBy(t => t.ChargePointId))
                {
                    var sessions = cpGroup.OrderBy(t => t.StartTime).ToList();

                    // Sessions that START within the reported month
                    var inPeriod = sessions
                        .Where(t => t.StartTime >= periodStartUtc && t.StartTime < periodEndUtc)
                        .ToList();

                    if (inPeriod.Count == 0)
                    {
                        // No sessions started this month for this charge point => nothing to report
                        continue;
                    }

                    var firstInPeriod = inPeriod.First();
                    double openingMeter = firstInPeriod.MeterStart;

                    // Tag that started the first session of the month (name, else id)
                    string firstTag = firstInPeriod.StartTagId;
                    if (!string.IsNullOrEmpty(firstTag) && tagNames.TryGetValue(firstTag, out var tn) && !string.IsNullOrEmpty(tn))
                    {
                        firstTag = tn;
                    }

                    string cpName = (cpNames.TryGetValue(cpGroup.Key, out var n) && !string.IsNullOrEmpty(n))
                        ? n : cpGroup.Key;

                    var row = new MonthlyChargeReportRow
                    {
                        ChargePointName = cpName,
                        FirstTagName = firstTag
                    };

                    // Closing anchor: the FIRST session that starts on/after the end of the month.
                    var nextAfterPeriod = sessions
                        .FirstOrDefault(t => t.StartTime >= periodEndUtc);

                    if (nextAfterPeriod != null)
                    {
                        // Clean telescoping close on the next month's first session start.
                        // (Valid whether that session is open or closed — only its StartMeter is used.)
                        double closingMeter = nextAfterPeriod.MeterStart;
                        row.EnergyKwh = SafeDelta(openingMeter, closingMeter);
                        row.SessionActive = false;
                    }
                    else
                    {
                        // No session after the month yet => fallback to last completed session's stop meter.
                        // But if the most recent in-period session is still OPEN (no stop meter), the meter
                        // is advancing inside an unfinished session => no reliable number => "session active".
                        var lastInPeriod = inPeriod.Last();
                        bool lastIsOpen = !lastInPeriod.MeterStop.HasValue || !lastInPeriod.StopTime.HasValue;

                        if (lastIsOpen)
                        {
                            row.EnergyKwh = null;
                            row.SessionActive = true;
                        }
                        else
                        {
                            double closingMeter = lastInPeriod.MeterStop.Value;
                            row.EnergyKwh = SafeDelta(openingMeter, closingMeter);
                            row.SessionActive = false;
                        }
                    }

                    vm.Rows.Add(row);
                }

                vm.Rows = vm.Rows.OrderBy(r => r.ChargePointName).ToList();
                return View(vm);
            }
            catch (Exception exp)
            {
                Logger.LogError(exp, "MonthlyChargeReport: Error generating report");
                TempData["ErrMessage"] = exp.Message;
                return RedirectToAction("Error", new { Id = "" });
            }
        }

        /// <summary>
        /// Meter delta with a guard against meter reset/replacement (negative delta).
        /// Returns null on a negative result so the view can flag it rather than
        /// show a nonsensical number.
        /// </summary>
        private static double? SafeDelta(double opening, double closing)
        {
            double d = closing - opening;
            if (d < 0)
            {
                return null; // meter went backwards (reset/replacement) — don't emit garbage
            }
            return Math.Round(d, 3);
        }
    }
}
