using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace _23DTHE3_DemoCoTuong.Controllers.api
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoTuongController : ControllerBase
    {
        private IWebHostEnvironment _environment;

        public CoTuongController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        [HttpGet("board")]
        public IActionResult actionResult() {
            try
            {
                var filePath = Path.Combine(
                   _environment.WebRootPath,
                   "co_tuong_initial.json"
               );
                var json = System.IO.File.ReadAllText(filePath);
                //kiem tra neu het hàng.....
                ///Ok(new { status = false, message = "sản phảm hết hàng" });

                return Ok(new { status = true, message = "", data = json });
            }
            catch (Exception ex) {
                //write log to database or file
                return Ok(new { status = false, message = "Có lỗi xảy ra" });

            }
           
        }
    }
}
