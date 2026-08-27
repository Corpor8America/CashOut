using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly CategoryService _categories;

    public CategoriesController(CategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _categories.GetAll());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");
        var category = await _categories.Create(req.Name);
        return Ok(category);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");
        var category = await _categories.Update(id, req.Name);
        return category == null ? NotFound() : Ok(category);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _categories.Delete(id);
        return deleted ? Ok() : NotFound();
    }

    public record UpsertRequest(string Name);
}
