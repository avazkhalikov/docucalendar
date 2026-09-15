using DocuCalendar.Application.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// The ready-made booking scripts for common kinds of business. Read-only: choosing one fills
/// the calendar's form on the client, and only Save writes anything.
/// </summary>
[ApiController]
[Route("api/calendars/templates")]
public sealed class TemplatesController : StaffControllerBase
{
    [HttpGet]
    public IActionResult List() =>
        Ok(new
        {
            templates = BookingTemplates.All.Select(t =>
            {
                var script = BookingScript.Parse(t.ScriptJson);
                return new
                {
                    key = t.Key,
                    name = t.Name,
                    blurb = t.Blurb,
                    slotMinutes = t.SlotMinutes,
                    maxMinutes = t.MaxMinutes,
                    script = new
                    {
                        instructions = script.Instructions,
                        questions = script.Questions.Select(q => new { ask = q.Ask, required = q.Required }),
                        services = script.Services.Select(s => new { name = s.Name, minutes = s.Minutes }),
                    },
                };
            }),
        });
}
