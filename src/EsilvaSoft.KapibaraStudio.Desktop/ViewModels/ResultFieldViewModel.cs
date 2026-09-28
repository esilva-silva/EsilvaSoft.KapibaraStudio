using System.Globalization;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed class ResultFieldViewModel
{
    private readonly string _name;
    private readonly JsonElement _value;
    private readonly IdentifierDisplayOptions _options;
    private readonly UuidDisplayValue _uuid;
    private readonly bool _isObjectId;
    private readonly string _objectId;
    private IReadOnlyList<ResultFieldViewModel>? _children;
    private readonly int? _pageOffset;

    public ResultFieldViewModel(string name, JsonElement value, UuidRepresentation representation = UuidRepresentation.Standard)
        : this(name, value, new IdentifierDisplayOptions(IdentifierRepresentationMode.Standard, representation)) { }

    public ResultFieldViewModel(string name, JsonElement value, IdentifierDisplayOptions options)
    {
        _name = name; _value = value; _options = options;
        _isObjectId = IdentifierRepresentationService.TryGetObjectId(value, out _objectId);
        _uuid = _isObjectId ? default : UuidCodec.Describe(value, options.Uuid);
    }

    public string Label => _pageOffset is not null ? _name : _name + ": " + (_isObjectId ? IdentifierRepresentationService.DescribeObjectId(_objectId, _options) : _uuid.Kind switch
    {
        UuidDisplayKind.Uuid => _uuid.Text,
        UuidDisplayKind.UnknownLegacy => LocalizationViewModel.Current.Resolve("unknownLegacyUuidValue"),
        _ => _value.ValueKind is JsonValueKind.Object ? "{…}" : _value.ValueKind is JsonValueKind.Array ? $"[{_value.GetArrayLength()}]" : _value.GetRawText()
    });
    public string Json => _value.GetRawText();
    private ResultFieldViewModel(JsonElement value, IdentifierDisplayOptions options, int offset)
    {
        _name = LocalizationViewModel.Current.Format("nextFields", offset); _value = value; _options = options; _pageOffset = offset; _objectId = "";
    }
    public IReadOnlyList<ResultFieldViewModel> Children => _children ??= _isObjectId || _uuid.Kind != UuidDisplayKind.NotUuid ? [] : CreateChildren(_value, _options, _pageOffset ?? 0);

    internal static IReadOnlyList<ResultFieldViewModel> CreateChildren(JsonElement value, IdentifierDisplayOptions options, int offset = 0)
    {
        const int pageSize = 256;
        var children = new List<ResultFieldViewModel>();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject().Skip(offset))
            {
                if (children.Count == pageSize) { children.Add(new(value, options, offset + pageSize)); break; }
                children.Add(new(property.Name, property.Value, options));
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray().Skip(offset))
            {
                if (children.Count == pageSize) { children.Add(new(value, options, offset + pageSize)); break; }
                children.Add(new((offset + children.Count).ToString(CultureInfo.InvariantCulture), item, options));
            }
        }
        return children;
    }

}
