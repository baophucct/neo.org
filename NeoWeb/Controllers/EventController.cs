﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeoWeb.Data;
using NeoWeb.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;

namespace NeoWeb.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EventController : Controller
    {
        private readonly ApplicationDbContext _context;
        private string _userId;
        private bool _userRules;
        private readonly IHostingEnvironment _env;

        public EventController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, IHostingEnvironment env)
        {
            _context = context;
            _userId = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _env = env;
            if (_userId != null)
            {
                _userRules = _context.UserRoles.Any(p => p.UserId == _userId);
            }
        }

        // GET: event
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Index(string k = null, int c = 0, int d = 0, string z = null)
        {
            var models = _context.Events.OrderBy(o => o.StartTime).Select(p => new Event()
            {
                Id = p.Id,
                Name = p.Name,
                Type = p.Type,
                Country = p.Country,
                City = p.City,
                Address = p.Address,
                StartTime = p.StartTime,
                EndTime = p.EndTime,
                Cover = p.Cover,
                Organizers = p.Organizers,
                IsFree = p.IsFree,
                ThirdPartyLink = p.ThirdPartyLink
            });
            //全部筛选列表
            ViewBag.Countries = models.Select(p => p.Country).Distinct();
            ViewBag.Types = models.Select(p => p.Type).Distinct();
            ViewBag.Dates = new string[] { "All Dates", "This Week", "This Month", "Past Events" };
            //筛选列表中的默认选项
            ViewBag.Keywords = k;
            ViewBag.CountryId = c;
            ViewBag.Date = d;
            //对关键词进行筛选
            if (!String.IsNullOrEmpty(k))
            {
                var keywords = k.Split(" ");
                foreach (var item in keywords)
                {
                    switch (item.ToLower())
                    {
                        case "conference": models = models.Where(p => p.Type == EventType.Conference); break;
                        case "meetup": models = models.Where(p => p.Type == EventType.Meetup); break;
                        case "workshop": models = models.Where(p => p.Type == EventType.Workshop); break;
                        case "hackathon": models = models.Where(p => p.Type == EventType.Hackathon); break;
                        default:
                            models = models.Where(p => p.Name.Contains(item)
                   || p.Country.Name.Contains(item, StringComparison.OrdinalIgnoreCase)
                   || p.Country.ZhName.Contains(item, StringComparison.OrdinalIgnoreCase)
                   || p.City.Contains(item, StringComparison.OrdinalIgnoreCase)
                   || p.Address.Contains(item, StringComparison.OrdinalIgnoreCase)
                   || p.Organizers.Contains(item, StringComparison.OrdinalIgnoreCase)); break;
                    }
                }
            }
            //对国家进行筛选
            if (c > 0)
            {
                models = models.Where(p => p.Country.Id == c);
            }
            //对日期进行筛选
            switch (d)
            {
                //本周内（非7天内）的未结束的活动
                case 1: models = models.Where(p => IsInSameWeek(DateTime.Now, p.StartTime) || IsInSameWeek(DateTime.Now, p.EndTime)).Where(p => p.EndTime >= DateTime.Now); break;
                //本月内（非30天内）的未结束的活动
                case 2: models = models.Where(p => p.StartTime.Year == DateTime.Now.Year && p.StartTime.Month == DateTime.Now.Month || p.EndTime.Year == DateTime.Now.Year && p.EndTime.Month == DateTime.Now.Month).Where(p => p.EndTime >= DateTime.Now); break;
                //已经结束的活动
                case 3: models = models.Where(p => p.EndTime < DateTime.Now); break;
            }
            //对具体日期进行查找
            if (DateTime.TryParse(z, out DateTime date))
                models = models.Where(p => p.StartTime.Date <= date && p.EndTime.Date >= date);


            ViewBag.UserRules = _userRules;
            return View(models);
        }

        [HttpGet]
        [AllowAnonymous]
        public JsonResult Date(int year, int month)
        {
            var obj = _context.Events.Where(p => p.StartTime.Year == year && p.StartTime.Month == month).OrderBy(p => p.StartTime).Select(p => p.StartTime.ToString("yyyy/M/d")).ToList().Distinct();
            return Json(obj);
        }

        /// <summary>   
        /// 判断两个日期是否在同一周   
        /// </summary>    
        private bool IsInSameWeek(DateTime dateX, DateTime dateY)
        {
            if (dateX > dateY)
            {
                var temp = dateX;
                dateX = dateY;
                dateY = temp;
            }
            double timespan = (dateY - dateX).TotalDays;
            int dayOfWeek = Convert.ToInt32(dateY.DayOfWeek);
            if (dayOfWeek == 0) dayOfWeek = 7;
            return timespan < 7 && timespan < dayOfWeek;
        }

        // GET: event/details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }
            ViewBag.UserRules = _userRules;
            var @event = await _context.Events.Include(m => m.Country)
                .SingleOrDefaultAsync(m => m.Id == id);
            if (@event == null)
            {
                return NotFound();
            }

            return View(@event);
        }

        // GET: event/create
        public IActionResult Create()
        {
            ViewBag.Countries = _context.Countries.ToList();
            return View();
        }

        // POST: event/create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,City,Type,Address,StartTime,EndTime,Cover,Details,Organizers,IsFree,ThirdPartyLink")] Event @event, int countryId, IFormFile cover)
        {
            var country = _context.Countries.FirstOrDefault(p => p.Id == countryId);
            if (country == null)
            {
                ModelState.AddModelError("Country", "国家错误");
            }
            @event.Country = country;
            if (@event.EndTime <= @event.StartTime)
            {
                ModelState.AddModelError("EndTime", "截止时间错误");
            }
            if (!@event.IsFree && @event.ThirdPartyLink == null)
            {
                ModelState.AddModelError("ThirdPartyLink", "付费活动需要填写购票链接");
            }
            if (ModelState.IsValid)
            {
                if (cover != null)
                {
                    @event.Cover = Upload(cover);
                }
                @event.Details = EventConvert(@event.Details);
                _context.Add(@event);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Countries = _context.Countries.ToList();
            return View(@event);
        }

        private string Upload(IFormFile cover)
        {
            var fileName = Helper.UploadMedia(cover, _env);
            Task.Run(()=> {
                var filePath = Path.Combine(_env.ContentRootPath, "wwwroot/upload", fileName);
                using (Image<Rgba32> image = Image.Load(filePath))
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(600, 600 * image.Height / image.Width),
                        Mode = ResizeMode.Max
                    }));
                    image.Save(filePath);
                }
            });
            return fileName;
        }

        // GET: event/edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            ViewBag.Countries = _context.Countries.ToList();
            if (id == null)
            {
                return NotFound();
            }

            var @event = await _context.Events.SingleOrDefaultAsync(m => m.Id == id);
            if (@event == null)
            {
                return NotFound();
            }
            return View(@event);
        }

        // POST: event/edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,City,Type,Address,StartTime,EndTime,Cover,Details,Organizers,IsFree,ThirdPartyLink")] Event @event, int countryId, string oldCover, IFormFile cover)
        {
            if (id != @event.Id)
            {
                return NotFound();
            }
            var country = _context.Countries.FirstOrDefault(p => p.Id == countryId);
            if (country == null)
            {
                ModelState.AddModelError("Country", "国家错误");
            }
            @event.Country = country;
            //var oldCover = _context.Events.FirstOrDefault(p => p.Id == @event.Id).Cover;
            if (@event.EndTime <= @event.StartTime)
            {
                ModelState.AddModelError("EndTime", "截止时间错误");
            }
            if (!@event.IsFree && @event.ThirdPartyLink == null)
            {
                ModelState.AddModelError("ThirdPartyLink", "付费活动需要填写购票链接");
            }
            if (ModelState.IsValid)
            {
                try
                {
                    if (cover != null)
                    {
                        if (!String.IsNullOrEmpty(@event.Cover))
                            System.IO.File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/upload", @event.Cover));
                        @event.Cover = Upload(cover);
                    }
                    else
                    {
                        @event.Cover = oldCover;
                    }
                    @event.Details = EventConvert(@event.Details);
                    _context.Update(@event);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!EventExists(@event.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Countries = _context.Countries.ToList();
            return View(@event);
        }

        // GET: event/delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var @event = await _context.Events.Include(m => m.Country)
                .SingleOrDefaultAsync(m => m.Id == id);
            if (@event == null)
            {
                return NotFound();
            }

            return View(@event);
        }

        // POST: event/delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var @event = await _context.Events.SingleOrDefaultAsync(m => m.Id == id);
            _context.Events.Remove(@event);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool EventExists(int id)
        {
            return _context.Events.Any(e => e.Id == id);
        }

        private string EventConvert(string input)
        {
            input = Regex.Replace(input, @"<!\-\-\[if gte mso 9\]>[\s\S]*<!\[endif\]\-\->", ""); //删除 ms office 注解
            input = Regex.Replace(input, "src=\".*/upload", "data-original=\"/upload"); //替换上传图片的链接
            input = Regex.Replace(input, "<img src=", "<img data-original="); //替换外部图片的链接
            return input;
        }
    }
}
