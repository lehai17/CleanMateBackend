using CleanMate.Api.Infrastructure;
using CleanMate.CleanerWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;

namespace CleanMate.CleanerWeb.Controllers
{
    public class BookingController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        public BookingController(AppDbContext db, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        // GET: /Booking/List
        public async Task<IActionResult> List()
        {
            var cleanerId = HttpContext.Session.GetInt32("CleanerId");
            var role = HttpContext.Session.GetString("Role");

            if (cleanerId == null || role != "Cleaner")
                return RedirectToAction("Login", "Auth");

            var bookings = await _db.Bookings
                .Include(b => b.User)
                //.Where(b => b.CleanerId == cleanerId)
                .OrderByDescending(b => b.StartTime)
                .Select(b => new BookingViewModel
                {
                    Id = b.Id,
                    OrderCode = "CM" + b.Id,
                    CustomerName = b.User.FullName,
                    StartTime = b.StartTime,
                    Price = b.Price,
                    Address = b.Address,
                    PaymentMethod = b.PaymentMethod,
                    DurationHours = b.DurationHours,
                    Notes = b.Notes,
                    Status = b.Status
                })
                .ToListAsync();

            return View(bookings);
        }

        // POST: /Booking/Accept
        [HttpPost]
        public async Task<IActionResult> Accept(int id)
        {
            var cleanerId = HttpContext.Session.GetInt32("CleanerId");
            if (cleanerId == null)
            {
                TempData["Message"] = " Please log in first.";
                return RedirectToAction("Login", "Auth");
            }

            // Gọi API Accept Booking (5238)
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri("http://localhost:5238");

            var response = await client.PutAsync($"/api/bookings/{id}/accept?cleanerId={cleanerId}", null);

            if (response.IsSuccessStatusCode)
                TempData["Message"] = $" Booking CM{id} accepted successfully!";
            else
                TempData["Message"] = $" Failed to accept booking (status: {response.StatusCode}).";

            return RedirectToAction("List");
        }
    }
}
