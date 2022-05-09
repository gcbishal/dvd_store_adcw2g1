﻿using dvd_store_adcw2g1.Models;
using dvd_store_adcw2g1.Models.Others;
using dvd_store_adcw2g1.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace dvd_store_adcw2g1.Controllers
{
    public class Functions : Controller
    {

        private readonly DatabaseContext _databasecontext;
        private readonly TextInfo _textInfo;

        public Functions(DatabaseContext context)
        {
            _databasecontext = context;
            _textInfo = new CultureInfo("en-US", false).TextInfo;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Function1(String lastName)
        {
            var query = from a in _databasecontext.Actors
                        join cm in _databasecontext.CastMembers
                        on a.ActorNumber equals cm.ActorNumber
                        join dt in _databasecontext.DVDTitles
                        on cm.DVDNumber equals dt.DVDNumber
                        where a.ActorSurname == lastName
                        select dt;
            return View(await query.ToListAsync());
        }

        public async Task<IActionResult> Function2(String lastName)
        {
            var query = from a in _databasecontext.Actors
                        join cm in _databasecontext.CastMembers
                        on a.ActorNumber equals cm.ActorNumber
                        join dt in _databasecontext.DVDTitles
                        on cm.DVDNumber equals dt.DVDNumber
                        join dc in _databasecontext.DVDCopies
                        on dt.DVDNumber equals dc.DVDNumber
                        join l in _databasecontext.Loans
                        on dc.CopyNumber equals l.CopyNumber
                        where a.ActorSurname == lastName && l.DateReturned != null
                        group l by dt into dtg
                        select new
                        {
                            DVDTitle = dtg.Key,
                            TotalDVDCopies = dtg.Count(),
                        };
            return View(await query.ToListAsync());
        }

        public IActionResult Function6Form()
        {
            ViewData["DVDCopies"] = new SelectList(_databasecontext.DVDCopies.Select(
            c => new
            {
                ID = c.DVDNumber,
                DVDCopyTitle = $"{c.DVDNumber} - {c.DVDTitle.DVDTitleName}"
            }), "ID", "DVDCopyTitle");
            ViewData["Members"] = new SelectList(_databasecontext.Members.Select(
            m => new
            {
                ID = m.MemberNumber,
                MemberTitle = $"{m.MemberNumber} - {m.MembershipFirstName} {m.MembershipLastName}"
            }), "ID", "MemberTitle");
            ViewData["LoanTypes"] = new SelectList(_databasecontext.LoanTypes.Select(
           t => new
           {
               ID = t.LoanTypeNumber,
               LoanTypeTitle = $"{t.LoanTypeNumber} - {t.LoanDuration}, {t.LoanDuration} days"
           }), "ID", "LoanTypeTitle");
            return View();
        }

        /// <summary>
        /// Allow to issue a DVD Copy on loan to a member.
        /// If confirm, Loan is saved
        /// Else, confirmation is asked with a message with loan charges.
        /// </summary>
        /// <param name="memberID">ID of the member who is taking a loan</param>
        /// <param name="dvdCopyID">ID of a DVDCopy record in Database</param>
        /// <param name="loanTypeID">ID of the Loan-Type chooosed by the member, while taking a DVD copy loan</param>
        /// <param name="confirm">Confirm Loan</param>
        /// <returns>Returns the Loan Record or null</returns>
        public async Task<IActionResult> Function6(int memberID, int dvdCopyID, int loanTypeID, bool confirm = false)
        {
            var memberRecord = await _databasecontext.Members.FirstOrDefaultAsync(m => m.MemberNumber == memberID);
            var dvdCopyRecord = await _databasecontext.DVDCopies.FirstOrDefaultAsync(c => c.DVDNumber == dvdCopyID);
            var loanTypeRecord = await _databasecontext.LoanTypes.FirstOrDefaultAsync(t => t.LoanTypeNumber == loanTypeID);
            if (memberRecord != null && dvdCopyRecord != null && loanTypeRecord != null)
            {
                var memberAge = (memberRecord.MemberDOB - DateTime.Now).TotalDays / 365;
                if (bool.Parse(dvdCopyRecord.DVDTitle.DVDCategory.AgeRestricted) && memberAge < 18)
                {
                    ViewData["message"] = "Does not meet Age Requirement!";
                    ViewData["error"] = true;
                }
                else
                {
                    var query = from l in _databasecontext.Loans
                                group l by l.MemberNumber into lg
                                select new
                                {
                                    MemberID = lg.Key,
                                    TotalActiveLoans = lg.Where(l => l.DateReturned == null).Count(),
                                };
                    var memberLoans = query.Where(l => l.MemberID == memberID).FirstOrDefault();
                    if (memberLoans == null || memberLoans.TotalActiveLoans < memberRecord.MembershipCategory.MembershipCategoryTotalLoans)
                    {
                        var dateNow = DateTime.Now;
                        var loanDuration = Int32.Parse(loanTypeRecord.LoanDuration);
                        if (confirm)
                        {
                            await _databasecontext.Loans.AddAsync(new Loan()
                            {
                                LoanType = loanTypeRecord,
                                DVDCopy = dvdCopyRecord,
                                Member = memberRecord,
                                DateOut = dateNow,
                                DateDue = dateNow.AddDays(loanDuration),
                                DateReturned = null,
                            });
                            await _databasecontext.SaveChangesAsync();
                            ViewBag.message = "Successfully Loaned!";
                            return RedirectToRoute(nameof(Index));
                        }
                        else
                        {
                            var dvdTitle = dvdCopyRecord.DVDTitle;
                            ViewData["message"] = $"The amount to pay for the {dvdTitle.DVDTitleName} copy is Rs.{dvdTitle.StandardCharge * loanDuration} as per the standard charge: Rs.{dvdTitle.StandardCharge}/day for {loanTypeRecord.LoanDuration} days dued at {dateNow.AddDays(loanDuration)}.";
                            ViewData["error"] = false;
                            ViewData["memberID"] = memberID;
                            ViewData["dvdCopyID"] = dvdCopyID;
                            ViewData["loanTypeID"] = loanTypeID;
                            ViewData["confirm"] = true;
                        }
                    }
                }
            }
            else
            {
                ViewData["message"] = "Something went wrong!";
                ViewData["error"] = true;
            }
            return View();
        }

        /// <summary>
        /// Confirm to record the return of a DVD copy. If due date is over the return date, the penalty charge is shown.
        /// </summary>
        /// <param name="dvdCopyID">ID of a DVDCopy record in Database</param>
        /// <returns>Returns the Loan Record or null</returns>
        public async Task<IActionResult> ConfirmFunction7(int? dvdCopyID)
        {
            var loan = await ValidateAndGetLoan(dvdCopyID);
            if (loan == null)
            {
                return NotFound();
            }
            if (loan.DateReturned == null)
            {
                var currentDate = DateTime.Now;
                var dueDate = loan.DateDue;
                if (currentDate > dueDate)
                {
                    var days = (currentDate - dueDate).TotalDays;
                    var penaltyChargeRate = loan.DVDCopy.DVDTitle.PenaltyCharge;
                    var penaltyAmount = days * penaltyChargeRate;
                    ViewData["message"] = $"{loan.Member.MembershipFirstName} is penalized with amount: {penaltyAmount} for exceeding due date by {days} days at {penaltyChargeRate}/days.";
                }
            }
            else
            {
                ViewData["message"] = "DVD Copy already returned";
            }
            return View();
        }

        /// <summary>
        /// Records the return of a DVD copy
        /// </summary>
        /// <param name="dvdCopyID">ID of a DVDCopy record in Database</param>
        /// <returns>Returns the Loan Record or null</returns>
        public async Task<IActionResult> Function7(int? dvdCopyID)
        {
            var loan = await ValidateAndGetLoan(dvdCopyID);
            if (loan == null)
            {
                return NotFound();
            }
            loan.DateReturned = DateTime.Now;
            _databasecontext.Loans.Update(loan);
            await _databasecontext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Validates DVDCopy ID and retrieves the Loan corresponding to the DVD copy that is not yet returned.
        /// </summary>
        /// <param name="dvdCopyID">ID of a DVDCopy record in Database</param>
        /// <returns>Returns the Loan Record or null</returns>
        public async Task<Loan?> ValidateAndGetLoan(int? dvdCopyID)
        {
            if (dvdCopyID == null)
            {
                return null;
            }
            var query = from l in _databasecontext.Loans
                        where l.CopyNumber == dvdCopyID && l.DateReturned == null
                        select l;
            return await query.FirstOrDefaultAsync();
        }

        /// <summary>
        /// Displays an alphabetic list of all members (including members with no current loans) with all their details(with membership category decoded) and the total number of DVDs they currently have on loan.This report should highlight with the message “Too many DVDs” any member who has more DVDs on loan than they are allowed from their MembershipCategor
        /// </summary>
        /// <returns>Renders Relevant View-Page</returns>
        public async Task<IActionResult> Function8()
        {
            var loans = _databasecontext.Loans;
            var Members = _databasecontext.Members;
            var query = from m in Members
                        join l in loans
                        on m.MemberNumber
                        equals l.MemberNumber into lm
                        select new MembersActiveLoans
                        {
                            Member = m,
                            MemberCategory = m.MembershipCategory,
                            TotalActiveLoans = lm.Where(loan => loan.DateReturned == null).Count(),
                        };

            return View(await query.ToListAsync());
        }

        /// <summary>
        /// Create a new DVD title with auto-assignment/creation of producer, studio, category and actors as per data in ModelForm.
        /// </summary>
        /// <param name="dvdTitle">NewDVDTitle ModelForm with required form-data for DVDTitle record addition in Database</param>
        /// <returns>Renders Relevant View-Page</returns>
        [HttpPost]
        public async Task<IActionResult> Function9([Bind("DVDTitleName,Producer,DVDCategory,Studio,Actors,DateReleased,StandardCharge,PenaltyCharge")] NewDVDTiTle dvdTitle)
        {

            String producer = ToTitleCase(dvdTitle.Producer);
            String dvdCategory = ToTitleCase(dvdTitle.DVDCategory);
            String studio = ToTitleCase(dvdTitle.Studio);
            List<String> actors = dvdTitle.Actors.ConvertAll(value => ToTitleCase(value));
            try
            {
                var producerRecord = (await _databasecontext.Producers.FirstOrDefaultAsync(p => p.ProducerName == producer))
                    ?? (await _databasecontext.Producers.AddAsync(new Producer() { ProducerName = producer })).Entity;
                var studioRecord = (await _databasecontext.Studios.FirstOrDefaultAsync(s => s.StudioName == studio))
                    ?? (await _databasecontext.Studios.AddAsync(new Studio() { StudioName = studio })).Entity;
                var dvdCategoryRecord = await _databasecontext.DVDCategories.FirstOrDefaultAsync(c => c.CategoryDescription == dvdCategory);
                var dvdTitleRecord = (await _databasecontext.DVDTitles.AddAsync(new DVDTitle()
                {
                    DVDTitleName = dvdTitle.DVDTitleName,
                    DateReleased = dvdTitle.DateReleased,
                    StandardCharge = dvdTitle.StandardCharge,
                    PenaltyCharge = dvdTitle.PenaltyCharge,
                    Producer = producerRecord,
                    Studio = studioRecord,
                    DVDCategory = dvdCategoryRecord!,
                })).Entity;
                actors.ForEach(async a =>
                {
                    List<String> _ = a.Split(' ').ToList();
                    var firstName = _.First();
                    _.RemoveAt(0);
                    var lastName = String.Join(' ', _);
                    var actorRecord = (await _databasecontext.Actors.FirstOrDefaultAsync(a => a.ActorFirstName == firstName && a.ActorSurname == lastName))
                    ?? (await _databasecontext.Actors.AddAsync(new Actor() { ActorFirstName = firstName, ActorSurname = lastName })).Entity;
                    _databasecontext.CastMembers.Add(new CastMember() { DVDTitle = dvdTitleRecord, Actor = actorRecord });
                });
                await _databasecontext.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (Exception /* ex */)
            {
                //Log the error (uncomment ex variable name and write a log.)
                ModelState.AddModelError("", "Unable to save. " +
                    "Try again, and if the problem persists, " +
                    "see your system administrator.");
            }
            return View(Index);
        }

        /// <summary>
        /// Title case the give string.
        /// </summary>
        /// <param name="string">A string value/text</param>
        /// <returns>Returns a title-cased string</returns>
        private String ToTitleCase(String @string) => _textInfo.ToTitleCase(@string.Trim());

        /// <summary>
        /// Displays a list of all DVD Copies which are more than a year old and which are not currently on loan b.
        /// </summary>
        /// <returns>Renders Relevant View-Page</returns>
        public async Task<IActionResult> Function10()
        {
            var dvdCopies = _databasecontext.DVDCopies;
            var loans = _databasecontext.Loans;
            var query = from l in loans
                        join d in dvdCopies
                        on l.CopyNumber equals d.CopyNumber
                        where l.DateReturned != null
                        select new OldDVDCopy()
                        {
                            DVDTitle = d.DVDTitle,
                            DVDCopy = d,
                            Loan = l,
                        };
            return View(await query.ToListAsync());
        }

        /// <summary>
        /// Delete a DVD copy (the assumption is that the staff use the list to remove the DVD copies from the shelves) 
        /// </summary>
        /// <param name="id">ID ~ CopyNumber of a DVDCopy record</param>
        /// <returns>Renders Relevant View-Page</returns>
        public async Task<IActionResult> Function10Delete(int? id)
        {
            // Casecade deletion of corresponding loan record
            var dvdCopy = await _databasecontext.DVDCopies.FirstOrDefaultAsync(c => c.CopyNumber == id);
            if (dvdCopy != null)
            {
                _databasecontext.DVDCopies.Remove(dvdCopy);
            }
            await _databasecontext.SaveChangesAsync();
            return View(nameof(Index));
        }

        /// <summary>
        /// Displays a list of all DVD copies on loan currently, along with total loans, ordered by the date out and title.
        /// </summary>
        /// <returns>Renders Relevant View-Page</returns>
        public async Task<IActionResult> Function11()
        {
            var dvdTitles = _databasecontext.DVDTitles;
            var dvdCopies = _databasecontext.DVDCopies;
            var loans = _databasecontext.Loans;
            var loansPerCopy = from l in loans
                               group l by l.CopyNumber into lg
                               select new
                               {
                                   CopyNumber = lg.Key,
                                   TotalLoans = lg.Count()
                               };

            var query = from t in dvdTitles
                        join d in dvdCopies
                        on t.DVDNumber equals d.DVDNumber
                        join l in loans
                        on d.CopyNumber equals l.CopyNumber
                        join c in loansPerCopy
                        on l.CopyNumber equals c.CopyNumber
                        where l.DateReturned == null
                        orderby l.DateDue, t.DVDTitleName
                        select new DVDCopyLoan()
                        {
                            DVDTitleName = t.DVDTitleName,
                            CopyNumber = d.CopyNumber,
                            MemberName = $"{l.Member.MembershipFirstName} {l.Member.MembershipLastName}",
                            DateOut = l.DateOut,
                            TotalLoans = c.TotalLoans,
                        };

            return View(await query.ToListAsync());
        }

        /// <summary>
        /// Displays a list of all Members who have not borrowed any DVD in the last 31 days, ignoring any Member who has never borrowed a DVD.
        /// </summary>
        /// <returns>Renders Relevant View-Page</returns>
        public async Task<IActionResult> Function12()
        {
            var loans = _databasecontext.Loans;
            var query = from l in loans
                        group l by l.MemberNumber into lg
                        let lastDateOut = lg.Max(a => a.DateOut)
                        where (DateTime.Now - lastDateOut).TotalDays > 31
                        let member = lg.First().Member
                        orderby lastDateOut
                        select new InActiveLoanMember()
                        {
                            MemberFirstName = member.MembershipFirstName,
                            MemberLastName = member.MembershipLastName,
                            MemberAddress = member.MembershipAddress,
                            LastDateOut = lastDateOut,
                            LastLoanedDVDTitleName = lg.MaxBy(l => l.DateOut)!.DVDCopy.DVDTitle.DVDTitleName,
                            DaysSinceLastLoaned = (int)(DateTime.Now - lastDateOut).TotalDays,
                        };
            return View(await query.ToListAsync());
        }


        /// <summary>
        /// Displays a list of all DVD titles in the shop where no copy of the title has been loaned in the last 31 days.
        /// </summary>
        /// <returns>Renders Relevant View-Page</returns>
        [Route("dvd/unloan/")]
        public async Task<IActionResult> Function13()
        {
            var loans = _databasecontext.Loans;
            var dvdCopies = _databasecontext.DVDCopies;
            var query = from c in dvdCopies
                        join l in loans
                        on c.CopyNumber equals l.CopyNumber
                        group l by c.DVDNumber into lg
                        where (DateTime.Now - lg.Max(a => a.DateOut)).TotalDays > 31
                        select lg.First().DVDCopy.DVDTitle;
            return View(await query.ToListAsync());
        }
    }
}
