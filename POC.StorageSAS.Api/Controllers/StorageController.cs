using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace POC.StorageSAS.Api.Controllers
{
    public class StorageController : Controller
    {
        // GET: StorageController
        public ActionResult Index()
        {
            return View();
        }

        // GET: StorageController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: StorageController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: StorageController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: StorageController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: StorageController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: StorageController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: StorageController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
