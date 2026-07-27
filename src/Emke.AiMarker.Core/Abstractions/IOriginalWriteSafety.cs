using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Abstractions;

public interface IOriginalWriteSafety
{
    void Validate(OutputPlanItem plan);
}
